using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace IobBackupAnalyzer.Core;

/// <summary>
/// Eine Fundstelle mit Zugangsdaten in einem Blockly-Shadow-Block. Der Wert selbst wird
/// nicht gespeichert — nur seine Länge (siehe <see cref="CredentialFinding"/>).
/// </summary>
/// <param name="Hidden">
/// true, wenn im selben Eingang zusätzlich ein echter Baustein steckt. Dann zeigt der Editor
/// den Shadow nicht an, und das erzeugte JavaScript enthält ihn nicht — der Wert steht nur
/// noch im XML, und damit im Skript und in jedem Backup.
/// </param>
/// <param name="BlockId">ID des Bausteins, an dessen Eingang der Shadow hängt.</param>
/// <param name="BlockType">Typ dieses Bausteins, z. B. <c>http_get</c>.</param>
/// <param name="Input">Name des Eingangs, z. B. <c>URL</c>.</param>
/// <param name="Path">Pfad im XML von der Wurzel bis zum Eingang.</param>
/// <param name="Masked">Die Fundstelle mit geschwärztem Wert.</param>
/// <param name="Reason">Woran sie erkannt wurde.</param>
public sealed record ShadowCredentialFinding(bool Hidden, string BlockId, string BlockType, string Input,
                                             string Path, string Masked, string Reason)
{
    public string HintText => Hidden
        ? "Zugangsdaten im verdeckten Shadow-Block (im Editor nicht sichtbar) – Baustein "
          + "herausziehen, Wert leeren, wieder einsetzen"
        : "Zugangsdaten im Shadow-Block (im Editor sichtbar) – Wert im Eingang leeren oder ersetzen";
}

/// <summary>
/// Sucht Zugangsdaten in Blockly-Shadow-Blöcken.
///
/// <b>Das Problem:</b> Ein Eingang wie die URL von <c>http_get</c> trägt einen Shadow-Block —
/// das graue Textfeld, in das man direkt tippt. Steckt man einen anderen Baustein hinein,
/// verschwindet der Shadow aus dem Editor und aus dem erzeugten JavaScript, bleibt aber mit
/// seinem alten Wert im XML stehen. Ein früher eingetipptes Passwort lebt so unsichtbar weiter.
/// Die Prüfung in <see cref="CredentialAnalyzer"/> liest das erzeugte JavaScript und kann
/// es deshalb grundsätzlich nicht finden.
///
/// <b>Erkannt wird auf zwei Wegen:</b>
/// <list type="number">
/// <item>Am Inhalt, mit denselben Mustern wie im Quelltext (<c>&amp;password=…</c>,
/// <c>http://benutzer:…@</c>, <c>Bearer …</c>). Der typische Fund: ein verdeckter Shadow mit
/// Passwort-Parameter in der URL von <c>http_get</c>.</item>
/// <item>Am Kontext: Der Eingang oder die Variable, der der Wert zugewiesen wird, heißt nach
/// einem Passwort, Token, Schlüssel oder einer PIN. Dann zählt der ganze Wert — bei
/// <c>math_number</c> ab vier Ziffern, sonst wäre jede 0 eine PIN. Für diesen Weg gab es im
/// Referenzbackup kein Beispiel; er deckt ab, was die Muster nicht sehen.</item>
/// </list>
/// Nicht gefunden wird ein Wert ohne jeden Hinweis im Inhalt oder im Namen — ein Passwort
/// in einem verdeckten Text ohne Bezeichnung ist von jedem anderen Text nicht zu unterscheiden.
///
/// Sichtbare Shadows werden ebenfalls gemeldet. Ihr Wert steht zwar auch im erzeugten
/// JavaScript, aber hier kommt der Ort im Baukasten dazu.
/// </summary>
public static class ShadowCredentialAnalyzer
{
    private static readonly HashSet<string> KeyWords = new(StringComparer.Ordinal)
    {
        "password", "passwort", "passwd", "pass", "pwd", "pw", "token", "secret",
        "apikey", "pin", "auth", "kennwort"
    };

    private static readonly Regex WordSplit = new(
        @"(?<=[a-z])(?=[A-Z])|[^A-Za-z0-9]+|(?<=[A-Za-z])(?=[0-9])|(?<=[0-9])(?=[A-Za-z])",
        RegexOptions.CultureInvariant, RegexLimits.MatchTimeout);

    /// <summary>
    /// Prüft ein Blockly-XML. Leere Liste bei defektem XML oder wenn eine Mustersuche zu
    /// lange braucht — ein einzelnes Skript darf den Ladevorgang nicht beenden.
    /// </summary>
    public static IReadOnlyList<ShadowCredentialFinding> Analyze(string xml)
    {
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            var doc = XDocument.Load(reader);

            var result = new List<ShadowCredentialFinding>();
            foreach (var shadow in doc.Descendants().Where(e => e.Name.LocalName == "shadow"))
                Inspect(shadow, result);
            return result;
        }
        catch (XmlException) { return Array.Empty<ShadowCredentialFinding>(); }
        catch (RegexMatchTimeoutException) { return Array.Empty<ShadowCredentialFinding>(); }
    }

    private static void Inspect(XElement shadow, List<ShadowCredentialFinding> result)
    {
        var input = shadow.Parent;
        if (input is null || input.Name.LocalName != "value") return;
        var owner = input.Parent;
        if (owner is null) return;

        var hidden = input.Elements().Any(e => e.Name.LocalName == "block");
        var inputName = (string?)input.Attribute("name") ?? "";
        var ownerType = (string?)owner.Attribute("type") ?? "";
        var ownerId = (string?)owner.Attribute("id") ?? "";
        var shadowType = (string?)shadow.Attribute("type") ?? "";
        var path = PathOf(input);

        // Der Kontext: der Eingang selbst oder die Variable, in die der Wert geschrieben wird.
        string? context = null;
        if (IsKeyName(inputName)) context = $"Eingang „{inputName}\"";
        else if (owner.Elements().FirstOrDefault(e => e.Name.LocalName == "field"
                                                     && (string?)e.Attribute("name") == "VAR") is { } v
                 && IsKeyName(v.Value))
            context = $"Variable „{v.Value}\"";

        void Report(string masked, string reason) =>
            result.Add(new ShadowCredentialFinding(hidden, ownerId, ownerType, inputName, path, masked, reason));

        foreach (var field in shadow.Elements().Where(e => e.Name.LocalName == "field"))
        {
            var value = field.Value;
            if (value.Length == 0) continue;

            if (shadowType == "math_number")
            {
                var digits = value.Trim();
                if (context is not null && digits.Length >= 4 && digits.All(char.IsAsciiDigit))
                    Report($"<{digits.Length} Ziffern>", context);
                continue;
            }

            if (shadowType != "text") continue;

            var hits = CredentialAnalyzer.ScanValue(value);
            foreach (var h in hits)
                Report(h.Masked, h.KindText);

            // Ohne Leerraum, wie im Quelltext: „Bitte Passwort eingeben" ist kein Passwort.
            if (hits.Count == 0 && context is not null && value.Length >= 4
                && !value.Any(char.IsWhiteSpace)
                && !CredentialAnalyzer.LooksLikePlaceholder(value))
                Report($"<{value.Length} Zeichen>", context);
        }
    }

    /// <summary>
    /// true, wenn ein Name nach Zugangsdaten klingt. Zerlegt wird in Wörter (Unterstrich,
    /// Bindestrich, Binnenmajuskel), damit <c>SPIN</c> oder <c>keypad</c> nicht anschlagen,
    /// <c>api_key</c>, <c>apiKey</c> und <c>PASSWORD</c> aber schon.
    /// </summary>
    internal static bool IsKeyName(string name)
    {
        if (name.Length == 0) return false;
        var words = WordSplit.Split(name).Where(w => w.Length > 0)
                             .Select(w => w.ToLowerInvariant()).ToList();
        for (var i = 0; i < words.Count; i++)
        {
            if (KeyWords.Contains(words[i])) return true;
            if (words[i] == "api" && i + 1 < words.Count && words[i + 1] == "key") return true;
        }

        // Ganz in Großbuchstaben ohne Trenner: PASSWORD, APIKEY, USERPASSWORD.
        var flat = name.ToLowerInvariant();
        return flat.Contains("password") || flat.Contains("passwort") || flat.Contains("apikey");
    }

    /// <summary>
    /// Der Weg zum Eingang: Bausteintypen und die Eingänge dazwischen, z. B.
    /// <c>on › STATEMENT › controls_if › DO0 › http_get › URL</c>. <c>next</c> wird
    /// übergangen — es verbindet Bausteine derselben Ebene.
    /// </summary>
    private static string PathOf(XElement input)
    {
        var parts = new List<string>();
        for (var e = input; e is not null; e = e.Parent)
        {
            switch (e.Name.LocalName)
            {
                case "value":
                case "statement":
                    parts.Add((string?)e.Attribute("name") ?? "?");
                    break;
                case "block":
                case "shadow":
                    parts.Add((string?)e.Attribute("type") ?? "?");
                    break;
            }
        }
        parts.Reverse();
        return string.Join(" › ", parts);
    }
}

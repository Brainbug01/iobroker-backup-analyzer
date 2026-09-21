namespace IobBackupAnalyzer.Core;

/// <summary>Worauf sich der Suchbegriff im Skripte-Tab bezieht.</summary>
public enum ScriptSearchMode
{
    /// <summary>Name und ioBroker-Pfad.</summary>
    NameAndPath,
    /// <summary>Der Quelltext — bei Blockly das dekodierte XML.</summary>
    Code
}

/// <summary>
/// UI-neutrale Logik des Skripte-Tabs: Filter, Sortierung, Vorschautext und die
/// Spaltendefinition. Geteilt von WinForms- und Avalonia-Oberfläche.
/// </summary>
public static class ScriptsPresenter
{
    public static readonly string[] Columns = { "Name", "ioBroker-Pfad", "Typ", "Status", "Hinweise", "Zugangsdaten" };

    public static readonly string[] SearchModeLabels = { "Name/Pfad", "Im Code suchen" };

    /// <summary>Auswahl des Skripttyps. Index 0 heißt „keine Einschränkung".</summary>
    public static readonly string[] TypeLabels = { "Alle", "Blockly", "JavaScript", "TypeScript" };

    /// <summary>
    /// Beschriftung des Knopfes, der die ganze Liste exportiert. Exportiert wird immer das,
    /// was gerade in der Liste steht — bei gesetztem Filter also nur die Treffer. Damit das
    /// niemanden überrascht, sagt der Knopf es selbst, statt weiter „Alle" zu behaupten.
    /// </summary>
    public static string ExportAllLabel(int shown, int total) =>
        shown == total ? "Alle exportieren" : $"Gefilterte exportieren ({shown:N0})";

    /// <summary>Beschriftung des Export-Umschalters; ausgeschaltet ist der Auslieferungszustand.</summary>
    public const string GeneratedJsLabel = "Bei Blockly auch das erzeugte JavaScript";

    /// <summary>Erklärung dazu als Tooltip — kurz genug, um sie auch anzuzeigen.</summary>
    public const string GeneratedJsHint =
        "Aus: Der Export enthält je Skript genau eine Datei — Blockly als .xml, " +
        "JavaScript und TypeScript als .js. Das ist die Fassung, die auch in ioBroker liegt.\n" +
        "Ein: Bei Blockly kommt zusätzlich das daraus erzeugte JavaScript dazu. Zum Lesen " +
        "und Durchsuchen nützlich, in ioBroker aber nicht bearbeitbar.";

    /// <summary>Beschriftung des Hinweis-Filters.</summary>
    public const string OnlyWithHintsLabel = "Nur mit Hinweisen";

    /// <summary>Was die Hinweis-Spalte meint — als Tooltip an Filter und Spalte.</summary>
    public const string HintsHint =
        "Auffälligkeiten im Blockly-Aufbau: ein Auslöser im Rumpf eines anderen Auslösers, "
      + "ein vom javascript-Adapter als abgelöst gekennzeichneter Baustein, ein Auslöser "
      + "ohne Inhalt.\n"
      + "Geprüft wird ausschließlich Blockly — dort hängt jeder Befund an einem Baustein "
      + "mit eigener ID. Bei JavaScript und TypeScript bleibt die Spalte leer.\n"
      + "Das ist keine Bewertung des Skripts, sondern eine Liste von Fundstellen.";

    /// <summary>Auswahl des Zugangsdaten-Filters. Index 0 heißt „keine Einschränkung".</summary>
    public static readonly string[] CredentialLabels =
        { "Alle", "Klartext oder gemischt", "Umgebaut", "Unbekannter Eintrag" };

    /// <summary>Was die Spalte „Zugangsdaten" meint — als Tooltip an Filter und Zeile.</summary>
    public const string CredentialsHint =
        "Seit Admin 8 gibt es unter System → Zugangsdaten eine zentrale Ablage für Passwörter "
      + "und Schlüssel. Ab javascript-Adapter 10.1.1 holt ein Skript sie von dort mit "
      + "SECRETS.Name.feld; in Blockly gibt es dafür einen eigenen Baustein in der Kategorie "
      + "„System\" (englisch „Access Data\").\n"
      + "umgebaut: Das Skript benutzt SECRETS, im Quelltext steht nichts mehr im Klartext.\n"
      + "Klartext: Im Quelltext steht ein Passwort, Token oder Schlüssel — in einer Adresse, "
      + "einer Zuweisung oder einer Authorization-Kopfzeile.\n"
      + "gemischt: beides. Meist ein Rest vom Umbau, auch in einer auskommentierten Zeile.\n"
      + "unbekannt: Das Skript nennt einen Eintrag, den es im Backup nicht gibt.\n"
      + "Gesucht wird nach Mustern, nicht nach den gespeicherten Werten — die bleiben "
      + "ungelesen. Ein Schlüssel in einer Variablen mit unauffälligem Namen wird deshalb "
      + "nicht gefunden. Die Fundwerte selbst werden nie angezeigt, nur ihre Länge.";

    /// <summary>
    /// Die Zeile unter der Zählzeile. Leer, wenn es nichts zu sagen gibt: bei einem
    /// Skript-Backup (keine Adapter-Version bekannt) und ohne Skripte.
    /// </summary>
    public static string CredentialLine(BackupData data)
    {
        var r = data.Credentials;
        if (data.Scripts.Count == 0) return "";

        if (!r.Supported)
        {
            // Ohne Versionsangaben — ein Skript-Backup — gibt es nichts zu behaupten.
            if (r.JavascriptVersion.Length == 0 || r.AdminVersion.Length == 0) return "";

            return !CredentialAnalyzer.AdminSupports(r.AdminVersion)
                ? "Zugangsdaten: nicht geprüft — die zentrale Ablage gibt es ab Admin "
                + $"{CredentialAnalyzer.MinAdminVersion.Major}, im Backup ist {r.AdminVersion}"
                : "Zugangsdaten: nicht geprüft — SECRETS gibt es ab javascript "
                + $"{CredentialAnalyzer.MinJavascriptVersion}, im Backup ist {r.JavascriptVersion}";
        }

        var umgebaut = data.Scripts.Count(s => s.Credentials is { Status: CredentialStatus.Converted });
        var klartext = data.Scripts.Count(s => HasPlaintext(s));
        var aktiv = data.Scripts.Count(s => s.Enabled && HasPlaintext(s));

        var text = $"Zugangsdaten (Admin {r.AdminVersion}, javascript {r.JavascriptVersion}): "
                 + $"{umgebaut} umgebaut · "
                 + (klartext == 0 ? "kein Klartext gefunden" : $"{klartext} mit Klartext, davon {aktiv} aktiv")
                 + $" · {r.Entries.Count} {(r.Entries.Count == 1 ? "Eintrag" : "Einträge")} angelegt";

        if (r.UnusedEntries.Count > 0) text += $", {r.UnusedEntries.Count} unbenutzt";
        if (r.InstancesWithSecretsOff.Count > 0) text += " · SECRETS ausgeschaltet";
        return text;
    }

    private static bool HasPlaintext(ScriptInfo s) =>
        s.Credentials is { Status: CredentialStatus.Plaintext or CredentialStatus.Mixed };

    /// <summary>Die Einzelheiten zur Zeile — als Tooltip. Nur Namen, nie Werte.</summary>
    public static string CredentialLineDetails(BackupData data)
    {
        var r = data.Credentials;
        if (!r.Supported) return CredentialsHint;

        var lines = new List<string>
        {
            r.Entries.Count == 0
                ? "Im Backup ist kein Eintrag unter System → Zugangsdaten angelegt."
                : "Angelegte Einträge: " + string.Join("; ", r.Entries.Select(e =>
                      e.Fields.Count == 0 ? e.Name : $"{e.Name} ({string.Join(", ", e.Fields)})"))
        };

        if (r.UnusedEntries.Count > 0)
            lines.Add("Von keinem Skript benutzt: " + string.Join(", ", r.UnusedEntries)
                    + " — ein Adapter kann den Eintrag trotzdem verwenden.");

        if (r.InstancesWithSecretsOff.Count > 0)
            lines.Add("In " + string.Join(", ", r.InstancesWithSecretsOff)
                    + " ist der Zugriff der Skripte auf die Zugangsdaten ausgeschaltet "
                    + "(Instanzeinstellung enableSecrets).");

        return string.Join("\n", lines) + "\n\n" + CredentialsHint;
    }

    /// <summary>
    /// Filtert nach Status, Typ und Suchbegriff. <paramref name="typeIndex"/> bezieht sich
    /// auf <see cref="TypeLabels"/>; 0 (oder ungültig) lässt alle Typen durch.
    /// </summary>
    public static List<ScriptInfo> Filter(IEnumerable<ScriptInfo> scripts, bool hideDisabled,
                                          int typeIndex, ScriptSearchMode mode, string? term,
                                          bool onlyWithHints = false, int credentialIndex = 0)
    {
        var q = scripts;

        if (hideDisabled) q = q.Where(s => s.Enabled);
        if (onlyWithHints) q = q.Where(s => s.Hints.Count > 0);

        // Bezieht sich auf CredentialLabels.
        q = credentialIndex switch
        {
            1 => q.Where(HasPlaintext),
            2 => q.Where(s => s.Credentials is { Status: CredentialStatus.Converted }),
            3 => q.Where(s => s.Credentials is { UnknownNames.Count: > 0 }),
            _ => q
        };

        if (typeIndex is > 0 and < 4)
        {
            var want = typeIndex switch
            {
                1 => ScriptEngine.Blockly,
                2 => ScriptEngine.JavaScript,
                _ => ScriptEngine.TypeScript
            };
            q = q.Where(s => s.Engine == want);
        }

        var t = (term ?? "").Trim();
        if (t.Length > 0)
            q = mode == ScriptSearchMode.Code
                ? q.Where(s => s.SearchableCode.Contains(t, StringComparison.OrdinalIgnoreCase))
                : q.Where(s => s.DisplayPath.Contains(t, StringComparison.OrdinalIgnoreCase)
                            || s.Id.Contains(t, StringComparison.OrdinalIgnoreCase));

        return q.ToList();
    }

    /// <summary>
    /// Sortiert nach Spaltenindex aus <see cref="Columns"/>. Ein negativer Index stellt den
    /// Grundzustand her: nach ioBroker-Pfad, also in der Ordnerreihenfolge des Systems.
    /// </summary>
    public static List<ScriptInfo> Sort(IEnumerable<ScriptInfo> scripts, int column, bool ascending)
    {
        var list = scripts as IList<ScriptInfo> ?? scripts.ToList();

        if (column < 0)
            return list.OrderBy(s => s.DisplayPath, StringComparer.OrdinalIgnoreCase).ToList();

        Func<ScriptInfo, string> key = column switch
        {
            0 => s => s.Name,
            1 => s => s.Id,
            2 => s => s.EngineText,
            3 => s => s.StatusText,
            4 => s => s.HintsText,
            _ => s => s.CredentialText
        };

        return ascending
            ? list.OrderBy(key, StringComparer.OrdinalIgnoreCase).ToList()
            : list.OrderByDescending(key, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Die Zählzeile. Bei aktiver Codesuche wird der Suchbegriff genannt — sonst wirkt eine
    /// stark verkürzte Liste wie ein Fehler.
    /// </summary>
    public static string CountText(int shown, BackupData data, ScriptSearchMode mode, string? term)
    {
        var total = data.Scripts.Count;
        var text = shown == total
            ? $"{total} Skripte   ({data.ScriptsEnabled} aktiv, {data.ScriptsDisabled} deaktiviert)"
            : $"{shown} von {total} Skripten";

        var t = (term ?? "").Trim();
        if (mode == ScriptSearchMode.Code && t.Length > 0)
            text += $"   ·   Codesuche nach „{t}\"";

        // Nur nennen, wenn es etwas zu nennen gibt: Eine Anlage ohne Befund soll keine
        // Zeile lesen müssen, die „0 mit Hinweisen" sagt.
        var withHints = data.Scripts.Count(s => s.Hints.Count > 0);
        if (withHints > 0) text += $"   ·   {withHints} mit Hinweisen";

        return text;
    }

    /// <summary>
    /// Deaktivierte Skripte gedämpft, defektes Blockly als Problem, Skripte mit Hinweisen
    /// als Warnung.
    ///
    /// Die Reihenfolge ist Absicht: Ein deaktiviertes Skript läuft nicht, sein Befund ist
    /// also nichts, was gerade Ärger macht — es bleibt gedämpft.
    /// </summary>
    public static RowEmphasis Emphasis(ScriptInfo s) =>
        s.BlocklyBroken ? RowEmphasis.Problem
        : !s.Enabled ? RowEmphasis.Muted
        : s.Hints.Count > 0 ? RowEmphasis.Warn
        : s.Credentials is { } c && (c.Findings.Count > 0 || c.UnknownNames.Count > 0) ? RowEmphasis.Warn
        : RowEmphasis.None;

    public static string[] Row(ScriptInfo s) =>
        new[] { s.Name, s.Id, s.EngineText, s.StatusText, s.HintsText, s.CredentialText };

    /// <summary>
    /// Die Befunde des gewählten Skripts ausformuliert — je Befund eine Begründung und die
    /// Block-ID zum Wiederfinden im Editor. Leer, wenn es nichts zu melden gibt.
    /// </summary>
    public static string HintDetails(ScriptInfo? script)
    {
        if (script is null) return "";

        var aufbau = StructureDetails(script);
        var zugang = CredentialDetails(script);
        return aufbau.Length > 0 && zugang.Length > 0 ? aufbau + "\n\n" + zugang : aufbau + zugang;
    }

    /// <summary>
    /// Die Zugangsdaten-Befunde des gewählten Skripts: jede Fundstelle mit Zeile und
    /// geschwärztem Wert, dazu die benutzten Einträge. Leer ohne Befund.
    /// </summary>
    public static string CredentialDetails(ScriptInfo script)
    {
        if (script.Credentials is not { } c
            || (c.Status == CredentialStatus.None && c.UnknownNames.Count == 0))
            return "";

        var blockly = script.Engine == ScriptEngine.Blockly;
        var lines = new List<string>();

        if (c.Findings.Count > 0)
        {
            lines.Add((c.Findings.Count == 1
                          ? "1 Stelle mit Zugangsdaten im Klartext"
                          : $"{c.Findings.Count} Stellen mit Zugangsdaten im Klartext")
                    + (blockly ? " (Zeilen im erzeugten JavaScript):" : ":"));
            lines.AddRange(c.Findings.Select(f =>
                $"• Zeile {f.Line}: {f.Masked}  ({f.KindText}{(f.InComment ? ", auskommentiert" : "")})"));
            lines.Add("Abhilfe: unter System → Zugangsdaten einen Eintrag anlegen und "
                    + (blockly
                        ? "den Text durch den Zugangsdaten-Baustein aus der Kategorie „System\" "
                        + "(englisch „Access Data\") ersetzen."
                        : "den Wert durch SECRETS.Name.feld ersetzen."));
        }

        var namen = c.Uses.Where(u => u.Name.Length > 0)
                          .GroupBy(u => u.Name, StringComparer.Ordinal)
                          .Select(g =>
                          {
                              var felder = g.Select(u => u.Field).Where(f => f.Length > 0)
                                            .Distinct().ToList();
                              return felder.Count == 0 ? g.Key : $"{g.Key} ({string.Join(", ", felder)})";
                          })
                          .ToList();
        if (namen.Count > 0)
            lines.Add("Aus der zentralen Ablage benutzt: " + string.Join("; ", namen));
        else if (c.Uses.Count > 0)
            lines.Add("Benutzt SECRETS; der Name des Eintrags ergibt sich erst zur Laufzeit.");

        foreach (var n in c.UnknownNames)
            lines.Add($"• Den Eintrag „{n}\" gibt es im Backup nicht (System → Zugangsdaten). "
                    + "Das Skript bekommt an dieser Stelle keinen Wert — Schreibweise prüfen, "
                    + "Groß- und Kleinschreibung zählt.");

        return string.Join("\n", lines);
    }

    private static string StructureDetails(ScriptInfo script)
    {
        if (script.Hints.Count == 0) return "";

        var lines = script.Hints
            .OrderBy(h => h.Kind)
            .Select(h => h.BlockId.Length == 0
                ? "• " + h.LongText
                : $"• {h.LongText}  (Baustein-ID: {h.BlockId})");

        var kopf = script.Hints.Count == 1
            ? "1 Hinweis zum Aufbau dieses Skripts:"
            : $"{script.Hints.Count} Hinweise zum Aufbau dieses Skripts:";

        return kopf + "\n" + string.Join("\n", lines);
    }

    /// <summary>
    /// Der Text der Vorschau. <paramref name="showXml"/> greift nur bei Blockly-Skripten;
    /// bei allen anderen wird immer der Quelltext gezeigt.
    /// </summary>
    public static string PreviewText(ScriptInfo? script, bool showXml)
    {
        if (script is null) return "";

        var hasXml = script.BlocklyXml is not null;
        var text = hasXml && showXml ? script.BlocklyXml! : script.CleanSource;

        return text.Length == 0 ? "(Dieses Skript enthält keinen Quelltext.)" : text;
    }

    /// <summary>true, wenn für dieses Skript zwischen XML und JavaScript umgeschaltet werden kann.</summary>
    public static bool HasXmlView(ScriptInfo? script) => script?.BlocklyXml is not null;

    /// <summary>
    /// Aufschrift des Kopierknopfes an der Vorschau.
    ///
    /// Sie nennt, was wirklich in der Zwischenablage landet, und das ist bei Blockly kein
    /// Detail: Je nach Umschaltung ist es der erzeugte Quelltext oder das XML — und nur
    /// das XML lässt sich in ioBroker wieder als Blockly einfügen.
    /// </summary>
    public static string CopyLabel(ScriptInfo? script, bool showXml) =>
        HasXmlView(script)
            ? (showXml ? "XML kopieren" : "JavaScript kopieren")
            : "Quelltext kopieren";

    /// <summary>Rückmeldung nach dem Kopieren — dieselbe in allen drei Oberflächen.</summary>
    public static string CopyDoneText(ScriptInfo script, bool showXml)
    {
        var was = HasXmlView(script) && showXml ? "XML" : "Quelltext";
        return $"{was} von „{script.Name}\" in die Zwischenablage kopiert.";
    }

    /// <summary>
    /// Meldungstext nach einem Export — samt Fehlern, falls welche auftraten.
    /// Genannt wird der angelegte Überordner, nicht der gewählte Zielordner.
    /// </summary>
    public static string ExportSummary(ScriptExporter.ExportResult result)
    {
        var msg = $"{result.Scripts} Skripte in {result.Files} Dateien exportiert nach:\n{result.RootDir}";

        if (result.Errors.Count > 0)
            msg += $"\n\n{result.Errors.Count} Skripte konnten nicht geschrieben werden:\n"
                 + string.Join("\n", result.Errors.Take(5));

        return msg;
    }
}

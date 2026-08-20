using System.Globalization;
using System.Text;

namespace IobBackupAnalyzer.Core;

/// <summary>
/// Erzeugt aus dem Ziel-Datenpunkt eines Alias einen Vorschlag für die
/// Konvertierungsfunktionen (common.alias.read/write), die man in ioBroker von Hand
/// anlegen würde.
///
/// Grundlage ist <see cref="IobObject.States"/> (common.states, die Wertetabelle des
/// Datenpunkts). Aus ihr wird eine Ternär-Kette mit allen gültigen Werten gebaut. Trägt die
/// Wertetabelle sprechende Labels, ist der Vorschlag fertig; sind Label und Rohwert gleich
/// (z. B. Zigbee liefert off/heat/auto ohne Übersetzung), ist es ein Gerüst, in das nur noch
/// die Anzeigetexte eingetragen werden müssen.
///
/// Bewusste Grenze: eine reine Zahlenumrechnung (val/5+21) steht in keinem Metadatum und
/// kann deshalb nicht erzeugt werden — das meldet <see cref="Result.CanGenerate"/> = false.
///
/// Das Werkzeug schreibt nichts ins ioBroker-System: Der Vorschlag ist Text zum Kopieren.
/// </summary>
public static class ConverterGenerator
{
    public sealed record Result(string Read, string Write, string Note, bool CanGenerate);

    public static Result Generate(IobObject? target)
    {
        if (target is null)
            return new Result("", "", "Ziel-Datenpunkt ist im Backup nicht vorhanden — kein Vorschlag möglich.", false);

        var numeric = string.Equals(target.CommonType, "number", StringComparison.OrdinalIgnoreCase);

        if (target.States is { Count: > 0 })
        {
            // read:  Gerätewert -> Anzeigelabel;  write: Anzeigelabel -> Gerätewert
            var read = BuildTernary(target.States, deviceLeft: true, numeric);
            var write = BuildTernary(target.States, deviceLeft: false, numeric);

            var identity = target.States.All(kv => kv.Key == kv.Value);
            var note = identity
                ? $"Gerüst aus {target.States.Count} gültigen Werten. Die Anzeigetexte (rechte Seite beim Lesen) " +
                  "noch durch deine Labels ersetzen, z. B. 'off' → 'Aus'."
                : $"Fertiger Vorschlag aus der Wertetabelle des Datenpunkts ({target.States.Count} Werte).";

            return new Result(read, write, note, true);
        }

        if (string.Equals(target.CommonType, "boolean", StringComparison.OrdinalIgnoreCase))
            return new Result(
                "val === true ? 'An' : 'Aus'",
                "val === 'An' ? true : false",
                "Boolescher Datenpunkt ohne Wertetabelle — An/Aus-Gerüst, Labels bei Bedarf anpassen.",
                true);

        var typeText = string.IsNullOrEmpty(target.CommonType) ? "unbekannt" : target.CommonType;
        return new Result("", "",
            $"Kein Aufzählungs-Datenpunkt (common.states fehlt, Typ: {typeText}). Eine Umrechnung wie " +
            "val / 5 + 21 lässt sich aus dem Backup nicht ableiten — die musst du selbst angeben.",
            false);
    }

    /// <summary>
    /// Baut die Ternär-Kette. <paramref name="deviceLeft"/> = true erzeugt die Leserichtung
    /// (Gerätewert links, Label rechts), false die Schreibrichtung (umgekehrt). Der jeweils
    /// nicht getroffene Fall gibt am Ende den unveränderten Wert zurück (…: val).
    /// </summary>
    private static string BuildTernary(IReadOnlyDictionary<string, string> states, bool deviceLeft, bool numeric)
    {
        var sb = new StringBuilder();

        foreach (var (device, label) in states)
        {
            var deviceTok = DeviceToken(device, numeric);
            var labelTok = StringToken(label);

            var (left, right) = deviceLeft ? (deviceTok, labelTok) : (labelTok, deviceTok);
            sb.Append("val === ").Append(left).Append(" ? ").Append(right).Append(" : ");
        }

        sb.Append("val");
        return sb.ToString();
    }

    /// <summary>
    /// Der Gerätewert wird bei einem Zahl-Datenpunkt unquotiert ausgegeben (val === 0),
    /// sonst als String (val === 'off'). Nur wenn er sich auch wirklich als Zahl lesen
    /// lässt — sonst sicherheitshalber quotiert.
    /// </summary>
    private static string DeviceToken(string value, bool numeric)
    {
        if (numeric && double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
            return value;
        return StringToken(value);
    }

    /// <summary>JS-String-Literal mit einfachem Anführungszeichen, korrekt escaped.</summary>
    private static string StringToken(string s) =>
        "'" + s.Replace("\\", "\\\\").Replace("'", "\\'") + "'";
}

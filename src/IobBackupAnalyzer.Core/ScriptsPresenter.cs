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
    public static readonly string[] Columns = { "Name", "ioBroker-Pfad", "Typ", "Status", "Hinweise" };

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

    /// <summary>
    /// Filtert nach Status, Typ und Suchbegriff. <paramref name="typeIndex"/> bezieht sich
    /// auf <see cref="TypeLabels"/>; 0 (oder ungültig) lässt alle Typen durch.
    /// </summary>
    public static List<ScriptInfo> Filter(IEnumerable<ScriptInfo> scripts, bool hideDisabled,
                                          int typeIndex, ScriptSearchMode mode, string? term,
                                          bool onlyWithHints = false)
    {
        var q = scripts;

        if (hideDisabled) q = q.Where(s => s.Enabled);
        if (onlyWithHints) q = q.Where(s => s.Hints.Count > 0);

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
            _ => s => s.HintsText
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
        : RowEmphasis.None;

    public static string[] Row(ScriptInfo s) =>
        new[] { s.Name, s.Id, s.EngineText, s.StatusText, s.HintsText };

    /// <summary>
    /// Die Befunde des gewählten Skripts ausformuliert — je Befund eine Begründung und die
    /// Block-ID zum Wiederfinden im Editor. Leer, wenn es nichts zu melden gibt.
    /// </summary>
    public static string HintDetails(ScriptInfo? script)
    {
        if (script is null || script.Hints.Count == 0) return "";

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

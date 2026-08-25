using System.Text;

namespace IobBackupAnalyzer.Core;

/// <summary>
/// Welche Dateien je Skript geschrieben werden.
///
/// Was ein Skript wirklich <em>ist</em>, steht in <c>common.engineType</c>: Bei Blockly sind
/// die Blöcke das Original und das JavaScript nur das, was der Adapter daraus erzeugt — im
/// Admin nicht einmal bearbeitbar. Bei JavaScript und TypeScript gibt es kein XML.
/// </summary>
public enum ScriptExportFormat
{
    /// <summary>
    /// Nur das Ursprungsformat: Blockly als <c>.xml</c>, alles andere als <c>.js</c>.
    /// Ergibt genau eine Datei je Skript — die Fassung, die auch in ioBroker liegt.
    /// </summary>
    OriginalOnly,

    /// <summary>
    /// Zusätzlich das aus Blockly erzeugte JavaScript. Zum Lesen und Durchsuchen nützlich,
    /// aber kein eigenständiges Skript.
    /// </summary>
    WithGeneratedJs
}

/// <summary>
/// Exportiert Skripte in eine Ordnerstruktur, die dem ioBroker-Skriptbaum entspricht.
///
/// Der gesamte Export liegt in einem Überordner <see cref="RootFolderName"/>, damit ein
/// beliebiger Zielordner (z. B. der Desktop) nicht mit Skriptordnern überschwemmt wird.
/// Ist die Quelldatei bekannt, liegt dieser Überordner seinerseits in einem Ordner mit dem
/// Namen des Backups (<see cref="BackupNaming"/>) — sonst überschreibt das zweite
/// ausgewertete Backup die Skripte des ersten.
/// Darunter entsteht exakt die ioBroker-Struktur: script.js.common.* bildet die Wurzel,
/// script.js.global.* liegt im Ordner "global" — genau wie im Admin.
///
/// Ob ein Skript deaktiviert ist, ändert die Ordnerstruktur bewusst nicht; es steht
/// nur als Namenszusatz an der Datei.
/// </summary>
public static class ScriptExporter
{
    /// <summary>Überordner, den der Export unterhalb des gewählten Zielordners anlegt.</summary>
    public const string RootFolderName = "ioBroker-Skripte";

    /// <summary>Namenszusatz für deaktivierte Skripte — die Ordnerstruktur bleibt davon unberührt.</summary>
    public const string DisabledSuffix = " (deaktiviert)";

    /// <param name="RootDir">Der tatsächlich beschriebene Überordner, nicht der gewählte Zielordner.</param>
    public sealed record ExportResult(int Files, int Scripts, List<string> Errors, string RootDir);

    /// <param name="sourceFile">
    /// Pfad der geladenen Backup-Datei. Nur für die Benennung des Überordners; wird nicht
    /// gelesen. Ohne Angabe bleibt es beim Aufbau ohne Backup-Ebene.
    /// </param>
    public static ExportResult Export(IEnumerable<ScriptInfo> scripts, string targetDir,
                                      ScriptExportFormat format = ScriptExportFormat.OriginalOnly,
                                      string? sourceFile = null)
    {
        var rootDir = BackupNaming.ExportRoot(targetDir, sourceFile, RootFolderName);
        Directory.CreateDirectory(rootDir);

        var files = 0;
        var count = 0;
        var errors = new List<string>();

        foreach (var s in scripts)
        {
            count++;
            try
            {
                var dir = rootDir;

                foreach (var segment in ExportPath(s).Split('/', StringSplitOptions.RemoveEmptyEntries))
                    dir = Path.Combine(dir, SanitizeFileName(segment));

                Directory.CreateDirectory(dir);
                var fileName = SanitizeFileName(s.Name) + (s.Enabled ? "" : DisabledSuffix);
                var baseName = Path.Combine(dir, fileName);

                var hasXml = s.BlocklyXml is not null;

                if (hasXml)
                {
                    File.WriteAllText(baseName + ".xml", s.BlocklyXml, new UTF8Encoding(false));
                    files++;
                }

                // Ohne XML ist das JavaScript das Original und wird immer geschrieben — das
                // gilt auch für ein Blockly-Skript, dessen XML im Backup defekt ist: sonst
                // bliebe von ihm gar nichts übrig.
                if (!hasXml || format == ScriptExportFormat.WithGeneratedJs)
                {
                    File.WriteAllText(baseName + ".js", s.CleanSource, new UTF8Encoding(false));
                    files++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                          or PathTooLongException or NotSupportedException)
            {
                errors.Add($"{s.DisplayPath}: {ex.Message}");
            }
        }

        return new ExportResult(files, count, errors, rootDir);
    }

    /// <summary>
    /// Ordnerpfad unterhalb des Überordners. <see cref="ScriptInfo.Folder"/> lässt den Bereich
    /// (common/global) weg, weil er in der Liste nur stören würde — für den Export muss er
    /// zurück, sonst landen "global/Meldung" und "Meldung" in derselben Datei.
    /// </summary>
    private static string ExportPath(ScriptInfo s)
    {
        var isGlobal = s.Id.StartsWith("script.js.global.", StringComparison.OrdinalIgnoreCase);
        if (!isGlobal) return s.Folder;

        return s.Folder.Length == 0 ? "global" : "global/" + s.Folder;
    }

    /// <summary>
    /// Ersetzt für das Dateisystem verbotene Zeichen. ioBroker erlaubt in Skriptnamen
    /// unter anderem "+" und ":" — in echten Backups kommen beide in Skriptnamen vor,
    /// meist als Teil einer Modell- oder Gerätebezeichnung.
    /// </summary>
    public static string SanitizeFileName(string name)
    {
        var sb = new StringBuilder(name.Length);
        var invalid = Path.GetInvalidFileNameChars();

        foreach (var c in name)
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);

        var result = sb.ToString().Trim().TrimEnd('.');
        return result.Length == 0 ? "_" : result;
    }

    /// <summary>Schreibt eine Tabelle als CSV mit Semikolon — so öffnet Excel sie direkt korrekt.</summary>
    public static void WriteCsv(string path, IEnumerable<string> headers, IEnumerable<string[]> rows)
    {
        // UTF-8 mit BOM, damit Excel Umlaute richtig darstellt.
        File.WriteAllText(path, CsvText(headers, rows), new UTF8Encoding(true));
    }

    /// <summary>
    /// Dieselbe Tabelle als Zeichenkette, ohne sie zu schreiben.
    ///
    /// Für die Browser-Fassung: Dort gibt es keinen Pfad, in den man schreiben könnte —
    /// der Text geht als Download an den Browser. Die Trennzeichen und die Maskierung
    /// stehen bewusst nur hier, damit alle drei Oberflächen zeichengleiche Dateien
    /// erzeugen.
    /// </summary>
    public static string CsvText(IEnumerable<string> headers, IEnumerable<string[]> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(";", headers.Select(EscapeCsv)));

        foreach (var row in rows)
            sb.AppendLine(string.Join(";", row.Select(EscapeCsv)));

        return sb.ToString();
    }

    private static string EscapeCsv(string value)
    {
        value ??= "";
        if (value.Contains(';') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return '"' + value.Replace("\"", "\"\"") + '"';
        return value;
    }
}

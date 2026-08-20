namespace IobBackupAnalyzer.Core;

/// <summary>
/// Leitet aus dem Pfad der geladenen Backup-Datei einen Namen ab, den Exporte und erzeugte
/// Dateien tragen können.
///
/// Hintergrund (Anwenderwunsch): Wer nacheinander mehrere Backups auswertet und in
/// denselben Zielordner exportiert, bekam bisher immer denselben Überordner
/// „ioBroker-Skripte" — der zweite Export überschrieb den ersten, ohne dass man später noch
/// erkennen konnte, aus welchem Backup der Inhalt stammt. Mit dem Backup-Namen als
/// Überordner liegen die Auswertungen nebeneinander und sind zuordenbar.
/// </summary>
public static class BackupNaming
{
    /// <summary>
    /// Endungen, die abgeschnitten werden. Reihenfolge ist wichtig: die längste zuerst,
    /// sonst bliebe von <c>…_backupiobroker.tar.gz</c> ein <c>.tar</c> stehen.
    /// </summary>
    private static readonly string[] Extensions =
    {
        ".tar.gz", ".tgz", ".tar", ".gz", ".zip", ".jsonl", ".json", ".js", ".xml"
    };

    /// <summary>
    /// Der Backup-Name ohne Endung, für das Dateisystem entschärft — oder ein leerer String,
    /// wenn sich kein sinnvoller Name ergibt (kein Pfad übergeben, oder der Name besteht nur
    /// aus einer Endung).
    /// </summary>
    public static string FolderName(string? sourceFile)
    {
        if (string.IsNullOrWhiteSpace(sourceFile)) return "";

        string name;
        try
        {
            name = Path.GetFileName(sourceFile.Trim());
        }
        catch (ArgumentException)
        {
            // Ein Pfad mit unzulässigen Zeichen ist kein Grund, den Export scheitern zu
            // lassen — dann eben ohne Backup-Ordner.
            return "";
        }

        foreach (var ext in Extensions)
        {
            if (!name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) continue;
            name = name[..^ext.Length];
            break;
        }

        var clean = ScriptExporter.SanitizeFileName(name);

        // SanitizeFileName liefert "_" für einen leeren Namen; das wäre als Ordnername
        // nichtssagend und schlechter als gar keiner.
        return clean is "_" or "" ? "" : clean;
    }

    /// <summary>
    /// Zielordner für einen Export: <c>&lt;Zielordner&gt;/&lt;Backup-Name&gt;/&lt;Überordner&gt;</c>.
    /// Der feste Überordner bleibt erhalten, damit Skript- und Datei-Export desselben Backups
    /// sich nicht vermischen. Ohne ermittelbaren Backup-Namen bleibt es beim bisherigen
    /// Aufbau ohne Zwischenebene.
    /// </summary>
    public static string ExportRoot(string targetDir, string? sourceFile, string rootFolderName)
    {
        var backup = FolderName(sourceFile);
        return backup.Length == 0
            ? Path.Combine(targetDir, rootFolderName)
            : Path.Combine(targetDir, backup, rootFolderName);
    }
}

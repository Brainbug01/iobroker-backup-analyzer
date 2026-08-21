namespace IobBackupAnalyzer.Core;

/// <summary>
/// Schutz für alles, was auf einen frei gewählten Zielpfad schreibt.
///
/// <b>Hintergrund:</b> Ein Speichern-Dialog steht oft im Ordner der Backups, und ein Klick
/// auf die falsche Zeile übernimmt deren Namen. Was danach folgt, ist unter Windows meist
/// harmlos (die Datei ist gesperrt), unter Linux und macOS aber nicht: Dort kürzt das
/// Öffnen zum Schreiben die Datei sofort auf 0 Byte — das Backup wäre weg, bevor der erste
/// Byte gelesen ist. Deshalb prüfen alle Exporte hier, bevor sie eine Datei anlegen.
/// </summary>
public static class ExportPaths
{
    /// <summary>Endungen, hinter denen ein Backup-Archiv stecken kann.</summary>
    private static readonly string[] ArchiveExtensions =
    {
        ".tar.gz", ".tgz", ".tar", ".gz"
    };

    /// <summary>
    /// Zwei Pfade, eine Datei? Verglichen wird der aufgelöste Vollpfad; unter Windows und
    /// macOS ohne Rücksicht auf Groß- und Kleinschreibung, unter Linux mit — dort sind
    /// <c>Backup.tar.gz</c> und <c>backup.tar.gz</c> zwei verschiedene Dateien.
    ///
    /// Ein unlesbarer Pfad gilt als „nicht dieselbe": Das Öffnen scheitert dann ohnehin
    /// gleich darauf mit einer verständlichen Meldung.
    /// </summary>
    public static bool IsSameFile(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;

        try
        {
            var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), comparison);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException
                                      or PathTooLongException or IOException
                                      or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Sieht der Zielpfad nach einem Backup-Archiv aus? Wer eine CSV-Liste oder ein
    /// Shell-Skript über eine <c>.tar.gz</c> schreibt, hat sich verklickt — und zwar
    /// unabhängig davon, ob es das gerade geladene Backup ist oder ein anderes.
    /// </summary>
    public static bool LooksLikeArchive(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        string name;
        try
        {
            name = Path.GetFileName(path.Trim());
        }
        catch (ArgumentException)
        {
            return false;
        }

        return ArchiveExtensions.Any(ext => name.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Rückfrage, bevor eine Archivdatei überschrieben wird — null, wenn nichts dagegen
    /// spricht. Der Text ist bewusst deutlich: Ein überschriebenes Backup ist nicht
    /// wiederherstellbar.
    /// </summary>
    public static string? ArchiveWarning(string? path)
    {
        if (!LooksLikeArchive(path)) return null;

        return $"Die Zieldatei sieht aus wie ein Backup-Archiv:\n{Path.GetFileName(path)}\n\n"
             + "Wird sie überschrieben, ist das Backup unwiederbringlich weg. "
             + "Trotzdem dorthin schreiben?";
    }
}

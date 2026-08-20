using System.Formats.Tar;
using System.IO.Compression;

namespace IobBackupAnalyzer.Core;

/// <summary>
/// Schreibt Dateien aus dem <c>files/</c>-Baum des Backups auf die Platte — das, was der
/// Admin unter „Dateien" zeigt.
///
/// Die Inhalte werden nicht beim Laden mitgenommen, sondern hier ein zweites Mal aus dem
/// Archiv gelesen. Ein Backup mit Kamerabildern brächte sonst zweistellige Megabytes in den
/// Arbeitsspeicher, die in aller Regel niemand anfasst. Ein Tar ist nur vorwärts lesbar,
/// deshalb läuft der Export in genau einem Durchlauf über das ganze Archiv und sammelt
/// dabei alles Angeforderte ein.
/// </summary>
public static class BackupFileExporter
{
    /// <summary>Überordner, den der Export unterhalb des gewählten Zielordners anlegt.</summary>
    public const string RootFolderName = "ioBroker-Dateien";

    /// <param name="Files">Tatsächlich geschriebene Dateien.</param>
    /// <param name="Bytes">Summe der geschriebenen Bytes.</param>
    /// <param name="Renamed">
    /// Dateien, deren Name für Windows entschärft werden musste. Kein Fehler, aber der
    /// Nutzer soll es wissen: ioBroker erlaubt in Dateinamen den Doppelpunkt, Windows nicht.
    /// </param>
    /// <param name="Missing">Angefordert, im Archiv aber nicht gefunden.</param>
    /// <param name="RootDir">Der tatsächlich beschriebene Überordner.</param>
    public sealed record ExportResult(int Files, long Bytes, int Renamed,
                                      List<string> Missing, List<string> Errors, string RootDir);

    public static ExportResult Export(BackupData data, IEnumerable<BackupFileInfo> files,
                                      string targetDir, CancellationToken ct = default)
    {
        var wanted = new Dictionary<string, BackupFileInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files) wanted[f.ArchivePath] = f;

        // Überordner mit dem Namen des Backups, damit zwei ausgewertete Backups im selben
        // Zielordner nebeneinander liegen statt einander zu überschreiben.
        var rootDir = BackupNaming.ExportRoot(targetDir, data.SourceFile, RootFolderName);
        Directory.CreateDirectory(rootDir);

        var written = 0;
        var bytes = 0L;
        var renamed = 0;
        var errors = new List<string>();
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (wanted.Count == 0)
            return new ExportResult(0, 0, 0, new List<string>(), errors, rootDir);

        if (!File.Exists(data.SourceFile))
        {
            errors.Add($"Die Backup-Datei ist nicht mehr erreichbar:\n{data.SourceFile}");
            return new ExportResult(0, 0, 0, wanted.Keys.ToList(), errors, rootDir);
        }

        using (var fs = File.OpenRead(data.SourceFile))
        using (Stream source = BackupLoader.LooksLikeGzip(data.SourceFile)
                   ? new GZipStream(fs, CompressionMode.Decompress)
                   : fs)
        using (var tar = new TarReader(source))
        {
            TarEntry? entry;
            while ((entry = ReadNextSafe(tar)) is not null)
            {
                ct.ThrowIfCancellationRequested();

                var name = BackupLoader.CleanEntryPath(entry.Name.Replace('\\', '/'));
                if (!wanted.TryGetValue(name, out var info)) continue;

                try
                {
                    var target = TargetPath(rootDir, info, out var wasRenamed);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                    using (var outFile = File.Create(target))
                    {
                        // Eine 0-Byte-Datei hat keinen Datenstrom — die leere Datei
                        // entsteht trotzdem, und genau so liegt sie auch in ioBroker.
                        entry.DataStream?.CopyTo(outFile);
                        bytes += outFile.Length;
                    }

                    written++;
                    if (wasRenamed) renamed++;
                    done.Add(name);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                              or PathTooLongException or NotSupportedException)
                {
                    errors.Add($"{info.DisplayPath}: {ex.Message}");
                    done.Add(name);
                }

                // Alles gefunden — der Rest des Archivs muss nicht mehr entpackt werden.
                if (done.Count == wanted.Count) break;
            }
        }

        var missing = wanted.Keys.Where(k => !done.Contains(k)).ToList();
        return new ExportResult(written, bytes, renamed, missing, errors, rootDir);
    }

    /// <summary>
    /// Zielpfad im Überordner: <c>&lt;Namensraum&gt;/&lt;Pfad&gt;</c>, jedes Segment für das
    /// Dateisystem entschärft. <paramref name="renamed"/> meldet, ob dabei etwas verändert
    /// wurde — typischerweise trifft das Kamerabilder, deren Name die Uhrzeit
    /// als <c>07:27:54</c> trägt: Windows erlaubt den Doppelpunkt nicht.
    /// </summary>
    private static string TargetPath(string rootDir, BackupFileInfo info, out bool renamed)
    {
        renamed = false;
        var path = rootDir;

        foreach (var segment in info.DisplayPath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var clean = ScriptExporter.SanitizeFileName(segment);
            if (!string.Equals(clean, segment, StringComparison.Ordinal)) renamed = true;
            path = Path.Combine(path, clean);
        }

        return path;
    }

    /// <summary>
    /// Ein abgeschnittenes Archiv beendet den Durchlauf, statt den ganzen Export scheitern
    /// zu lassen — was bis dahin gelesen wurde, ist geschrieben. Was fehlt, steht danach
    /// in <c>Missing</c>.
    /// </summary>
    private static TarEntry? ReadNextSafe(TarReader tar)
    {
        try
        {
            return tar.GetNextEntry();
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or IOException)
        {
            return null;
        }
    }
}

using System.Formats.Tar;
using System.IO.Compression;

namespace IobBackupAnalyzer.Core;

/// <summary>
/// Packt ein VIS-Projekt aus dem Backup in eine ZIP-Datei, die VIS über
/// <c>Tools → Projektimport</c> wieder einliest.
///
/// <b>Wozu:</b> Wer eine View gelöscht hat, musste bisher das ganze Backup zurückspielen
/// oder die vis-views.json von Hand in einen neuen Projektordner legen. Mit dieser ZIP
/// wird daraus: Backup öffnen, Projekt wählen, importieren — unter einem <i>neuen</i>
/// Projektnamen, sodass das laufende Projekt unangetastet bleibt und die verlorene View
/// per „Views → Exportieren/Importieren" herübergeholt werden kann.
///
/// <b>Aufbau der ZIP</b> (an je einem echten Export aus VIS 1.5.6 und VIS 2 abgeglichen,
/// beide Versionen identisch): der Inhalt des Projektordners liegt flach in der Wurzel,
/// Unterordner mit relativem Pfad. Kein Ordner mit dem Projektnamen, keine Metadatei.
/// <code>
///   Heizung.png
///   vis-user.css
///   vis-views.json
///   img/
///   img/ablauf.mp4
/// </code>
/// </summary>
public static class VisProjectExporter
{
    /// <summary>Die Datei, die ein Ordner unter vis.0 haben muss, um ein Projekt zu sein.</summary>
    public const string ViewsFileName = "vis-views.json";

    /// <summary>
    /// Ein VIS-Projekt im Backup — ein Ordner unterhalb von <c>vis.0</c> bzw.
    /// <c>vis-2.0</c> samt allem, was darin liegt.
    /// </summary>
    /// <param name="Namespace">Der Namensraum im Backup, z. B. <c>vis-2.0</c>.</param>
    /// <param name="Name">Der Projektname, also der Ordner darunter — meist <c>main</c>.</param>
    /// <param name="Files">Alle Dateien des Projekts, die vis-views.json eingeschlossen.</param>
    public sealed record VisProject(VisVersion Version, string Namespace, string Name,
                                    List<BackupFileInfo> Files)
    {
        public string VersionText => Version == VisVersion.Vis1 ? "VIS 1" : "VIS 2";

        /// <summary>Die vis-views.json des Projekts — null, wenn der Ordner keine hat.</summary>
        public BackupFileInfo? Views =>
            Files.FirstOrDefault(f => string.Equals(f.Name, ViewsFileName,
                                                    StringComparison.OrdinalIgnoreCase));

        /// <summary>Beiwerk neben den Views: Bilder, CSS, HTML — alles, was Widgets brauchen.</summary>
        public List<BackupFileInfo> Assets =>
            Files.Where(f => !string.Equals(f.Name, ViewsFileName,
                                            StringComparison.OrdinalIgnoreCase)).ToList();

        public long Bytes => Files.Sum(f => f.Size);

        /// <summary>
        /// Beschriftung in der Projektauswahl, z. B.
        /// <c>VIS 1 · main — 3,5 MB, 5 Dateien</c>.
        /// </summary>
        public string Label =>
            $"{VersionText} · {Name} — {BackupFileInfo.FormatSize(Bytes)}, " +
            VisPresenter.Count(Files.Count, "Datei", "Dateien");

        /// <summary>
        /// Vorgeschlagener Dateiname — und damit zugleich der Projektname, den VIS beim
        /// Import vergibt.
        ///
        /// <b>Das ist der wichtigste Punkt am ganzen Export.</b> An VIS 1.5.6 nachgemessen:
        /// Sobald die Datei in den Import-Dialog gezogen wird, trägt VIS den Projektnamen
        /// selbst ein — den Dateinamen ohne führendes Datum, aus <c>2026-08-21-main.zip</c>
        /// also <c>main</c> (genau so exportiert VIS auch selbst). Das Feld lässt sich
        /// danach ändern, aber es ist vorbelegt, und wer nicht darauf achtet, importiert
        /// unter diesem Namen.
        ///
        /// Daraus folgen zwei Festlegungen — beide schützen den vorbelegten Namen davor,
        /// im Ernstfall der falsche zu sein:
        /// <list type="bullet">
        /// <item>Der Projektname bleibt nie bei <c>main</c> stehen — die VIS-Version hängt
        /// dahinter. Sonst ersetzte ein unbeachtet bestätigter Import das laufende
        /// Projekt.</item>
        /// <item>Das Datum steht <b>hinten</b>, nicht vorn: Vorn würde VIS es abschneiden,
        /// und zwei Importe aus verschiedenen Backups ergäben denselben Projektnamen —
        /// der zweite überschriebe den ersten.</item>
        /// </list>
        /// Ergebnis: <c>main-vis1-2026-08-18.zip</c> wird zum Projekt
        /// <c>main-vis1-2026-08-18</c>.
        /// </summary>
        public string SuggestedFileName(DateTime? backupDate)
        {
            var version = Version == VisVersion.Vis1 ? "vis1" : "vis2";
            var date = backupDate?.ToString("yyyy-MM-dd") ?? "";
            var stem = date.Length > 0 ? $"{Name}-{version}-{date}" : $"{Name}-{version}";
            return ScriptExporter.SanitizeFileName(stem) + ".zip";
        }
    }

    /// <param name="Files">Tatsächlich in die ZIP geschriebene Dateien.</param>
    /// <param name="Bytes">Summe der ungepackten Bytes.</param>
    /// <param name="ZipBytes">Größe der fertigen ZIP-Datei.</param>
    /// <param name="Missing">Angefordert, im Archiv aber nicht gefunden.</param>
    /// <param name="ViewsIncluded">Ob die vis-views.json tatsächlich enthalten ist.</param>
    public sealed record ZipResult(string ZipPath, int Files, long Bytes, long ZipBytes,
                                   List<string> Missing, List<string> Errors,
                                   bool ViewsIncluded);

    /// <summary>
    /// Alle VIS-Projekte im Backup, nach Version und Name sortiert.
    ///
    /// Maßstab ist die vis-views.json: <b>Nicht jeder Ordner unter vis.0 ist ein Projekt.</b>
    /// Neben <c>main</c> liegen dort oft Ordner, die nur Bilder für einzelne Widgets
    /// enthalten (etwa <c>kamerabilder</c>). Als Projekt angeboten, ergäben sie eine ZIP,
    /// die der Projektimport nicht annimmt.
    /// </summary>
    public static List<VisProject> FindProjects(BackupData data)
    {
        var byKey = new Dictionary<string, VisProject>(StringComparer.OrdinalIgnoreCase);

        foreach (var f in data.Files)
        {
            if (VersionOf(f.Namespace) is not { } version) continue;

            // Dateien direkt im Namensraum (vis-2.0/vis-common-user.css) gehören keinem
            // Projekt, sondern allen — sie bleiben draußen.
            var slash = f.Path.IndexOf('/');
            if (slash <= 0) continue;

            var project = f.Path[..slash];
            var key = f.Namespace + "/" + project;

            if (!byKey.TryGetValue(key, out var entry))
            {
                entry = new VisProject(version, f.Namespace, project, new List<BackupFileInfo>());
                byKey[key] = entry;
            }

            entry.Files.Add(f);
        }

        return byKey.Values
                    .Where(p => p.Views is not null)
                    .OrderBy(p => p.Version)
                    .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
    }

    /// <summary>
    /// Ordner, die im selben Namensraum neben dem Projekt liegen, aber nicht dazugehören —
    /// typischerweise Bilderordner wie <c>vis.0/kamerabilder</c>.
    ///
    /// Sie stehen nicht in der ZIP, und das ist richtig so: VIS legt beim Import alles in
    /// den Projektordner, dort gehören sie nicht hin. Wer die ZIP aber auf einer
    /// <i>anderen</i> Anlage einspielt, muss sie getrennt mitnehmen — sonst zeigen Widgets
    /// auf Bilder, die es dort nicht gibt. Deshalb werden sie in der Meldung benannt.
    /// </summary>
    public static List<string> SiblingFolders(BackupData data, VisProject project)
    {
        var projects = FindProjects(data)
            .Where(p => string.Equals(p.Namespace, project.Namespace, StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return data.Files
            .Where(f => string.Equals(f.Namespace, project.Namespace, StringComparison.OrdinalIgnoreCase))
            .Select(f => f.Path.IndexOf('/') is var slash && slash > 0 ? f.Path[..slash] : "")
            .Where(folder => folder.Length > 0 && !projects.Contains(folder))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(folder => folder, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Die VIS-Version eines Namensraums — oder null, wenn er zu keiner VIS-Instanz gehört.
    /// <c>vis-2.</c> muss vor <c>vis.</c> geprüft werden, sonst matcht „vis" beides.
    /// Instanznummern über 0 gibt es selten, aber es gibt sie.
    /// </summary>
    private static VisVersion? VersionOf(string ns)
    {
        if (IsInstanceOf(ns, "vis-2")) return VisVersion.Vis2;
        if (IsInstanceOf(ns, "vis")) return VisVersion.Vis1;
        return null;
    }

    /// <summary>Prüft „&lt;adapter&gt;.&lt;zahl&gt;", z. B. vis.0 oder vis-2.1.</summary>
    private static bool IsInstanceOf(string ns, string adapter)
    {
        if (!ns.StartsWith(adapter + ".", StringComparison.OrdinalIgnoreCase)) return false;

        var rest = ns[(adapter.Length + 1)..];
        return rest.Length > 0 && rest.All(char.IsAsciiDigit);
    }

    /// <summary>
    /// Schreibt das Projekt als ZIP nach <paramref name="zipPath"/>.
    ///
    /// Die Inhalte werden — wie beim Datei-Export — erst hier aus dem Archiv nachgelesen,
    /// in genau einem Durchlauf: Ein Tar ist nur vorwärts lesbar, und eine Projektdatei
    /// mit Videos brächte sonst zweistellige Megabytes in den Arbeitsspeicher.
    /// </summary>
    /// <param name="includeAssets">
    /// false lässt alles außer der vis-views.json weg. Sinnvoll, wenn nur Views
    /// zurückgeholt werden sollen und Bilder und CSS in der Anlage noch liegen — die
    /// 3,5-MB-JSON ohne 2 MB Video daneben.
    /// </param>
    public static ZipResult Export(BackupData data, VisProject project, string zipPath,
                                   bool includeAssets, CancellationToken ct = default)
    {
        var source = includeAssets ? project.Files : Listed(project.Views);

        var wanted = new Dictionary<string, BackupFileInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in source) wanted[f.ArchivePath] = f;

        var errors = new List<string>();

        if (wanted.Count == 0)
        {
            errors.Add($"Das Projekt „{project.Name}\" enthält keine {ViewsFileName}.");
            return new ZipResult(zipPath, 0, 0, 0, new List<string>(), errors, false);
        }

        if (!File.Exists(data.SourceFile))
        {
            errors.Add($"Die Backup-Datei ist nicht mehr erreichbar:\n{data.SourceFile}");
            return new ZipResult(zipPath, 0, 0, 0, wanted.Keys.ToList(), errors, false);
        }

        // Quelle und Ziel dürfen nie dieselbe Datei sein. Unter Windows griffe zwar die
        // Dateisperre, unter Linux und macOS aber nicht: Dort kürzt File.Create das Ziel
        // sofort auf 0 Byte — das Backup wäre weg, bevor der erste Eintrag gelesen ist.
        if (ExportPaths.IsSameFile(zipPath, data.SourceFile))
        {
            errors.Add("Quelle und Ziel sind dieselbe Datei. Bitte einen anderen Namen wählen — " +
                       "sonst würde das Backup selbst überschrieben.");
            return new ZipResult(zipPath, 0, 0, 0, wanted.Keys.ToList(), errors, false);
        }

        var written = 0;
        var bytes = 0L;
        var viewsWritten = false;
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(zipPath))!);

        // Erst in eine Nebendatei schreiben, am Ende umbenennen: Bricht der Lauf ab —
        // Abbruchtaste, volle Platte, abgeschnittenes Archiv —, liegt am Zielort sonst
        // eine halbe ZIP, die wie ein fertiges Ergebnis aussieht.
        var temp = zipPath + ".teil";

        try
        {
            using (var zipFile = File.Create(temp))
            using (var zip = new ZipArchive(zipFile, ZipArchiveMode.Create))
            using (var fs = File.OpenRead(data.SourceFile))
            using (Stream tarSource = BackupLoader.LooksLikeGzip(data.SourceFile)
                       ? new GZipStream(fs, CompressionMode.Decompress)
                       : fs)
            using (var tar = new TarReader(tarSource))
            {
                TarEntry? entry;
                while ((entry = ReadNextSafe(tar)) is not null)
                {
                    ct.ThrowIfCancellationRequested();

                    var name = BackupLoader.CleanEntryPath(entry.Name.Replace('\\', '/'));
                    if (!wanted.TryGetValue(name, out var info)) continue;

                    var relative = RelativePath(project, info);

                    try
                    {
                        // Ordnereinträge nachbilden, wie VIS sie im eigenen Export schreibt
                        // („img/" als leerer Eintrag vor „img/ablauf.mp4").
                        foreach (var dir in ParentDirs(relative))
                            if (dirs.Add(dir)) zip.CreateEntry(dir + "/");

                        var zipEntry = zip.CreateEntry(relative, CompressionLevel.Optimal);

                        // Zeitstempel aus dem Archiv übernehmen, nicht die Uhrzeit des
                        // Exports: So sieht man der entpackten Datei an, von wann sie
                        // stammt. Das ZIP-Format kennt keine Zeiten vor 1980 — ein
                        // Archiveintrag ohne brauchbare Zeit behält die Vorgabe.
                        var stamp = entry.ModificationTime;
                        if (stamp.Year >= 1980) zipEntry.LastWriteTime = stamp;

                        using (var target = zipEntry.Open())
                        {
                            // Eine 0-Byte-Datei hat keinen Datenstrom — der leere Eintrag
                            // entsteht trotzdem, und genau so liegt sie auch in ioBroker
                            // (die vis-user.css im Referenz-Export ist genau das).
                            entry.DataStream?.CopyTo(target);
                        }

                        written++;
                        bytes += info.Size;
                        if (string.Equals(relative, ViewsFileName, StringComparison.OrdinalIgnoreCase))
                            viewsWritten = true;
                    }
                    catch (Exception ex) when (ex is IOException or NotSupportedException
                                                  or InvalidDataException)
                    {
                        errors.Add($"{info.DisplayPath}: {ex.Message}");
                    }

                    done.Add(name);

                    // Alles beisammen — der Rest des Archivs muss nicht mehr entpackt werden.
                    if (done.Count == wanted.Count) break;
                }
            }

            File.Move(temp, zipPath, overwrite: true);
        }
        catch
        {
            Delete(temp);
            throw;
        }

        var zipBytes = new FileInfo(zipPath).Length;
        var missing = wanted.Keys.Where(k => !done.Contains(k)).ToList();

        return new ZipResult(zipPath, written, bytes, zipBytes, missing, errors, viewsWritten);
    }

    /// <summary>Räumt die Nebendatei weg; ein Fehlschlag darf den echten nicht verdecken.</summary>
    private static void Delete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Bleibt liegen — sie heißt „.teil" und ist als Bruchstück erkennbar.
        }
    }

    private static List<BackupFileInfo> Listed(BackupFileInfo? file) =>
        file is null ? new List<BackupFileInfo>() : new List<BackupFileInfo> { file };

    /// <summary>
    /// Pfad der Datei relativ zum Projektordner — das, was in der ZIP steht.
    ///
    /// Bewusst <b>ohne</b> die Windows-Entschärfung des Datei-Exports: Die ZIP geht zurück
    /// nach ioBroker, nicht auf eine Windows-Platte. Ein Doppelpunkt im Dateinamen ist dort
    /// erlaubt, und ein umbenanntes Bild fände das Widget nicht mehr.
    /// </summary>
    private static string RelativePath(VisProject project, BackupFileInfo info)
    {
        var path = info.Path.Replace('\\', '/');
        var prefix = project.Name + "/";

        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? path[prefix.Length..]
            : path;
    }

    /// <summary>Alle Ordnerebenen über einer Datei, von außen nach innen.</summary>
    private static IEnumerable<string> ParentDirs(string relative)
    {
        var parts = relative.Split('/');
        for (var i = 1; i < parts.Length; i++)
            yield return string.Join('/', parts.Take(i));
    }

    /// <summary>
    /// Ein abgeschnittenes Archiv beendet den Durchlauf, statt den ganzen Export scheitern
    /// zu lassen — was bis dahin gelesen wurde, steht in der ZIP. Was fehlt, meldet
    /// <see cref="ZipResult.Missing"/>.
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

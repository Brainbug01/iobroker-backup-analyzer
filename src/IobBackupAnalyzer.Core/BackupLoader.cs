using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IobBackupAnalyzer.Core;

/// <summary>Meldung, dass die Eingabedatei kein verwertbares ioBroker-Backup ist.</summary>
public sealed class NotABackupException : Exception
{
    public NotABackupException(string message) : base(message) { }
}

/// <summary>
/// Lädt ein Backitup-Archiv oder eine entpackte JSON-Datei.
///
/// Die Formaterkennung erfolgt am Inhalt, nicht am Dateinamen. Unterstützt:
///   • objects.jsonl + states.jsonl  (aktuelles Backitup — der Regelfall)
///   • backup.json                   (ältere Backitup-Versionen)
///   • script.json                   (JavaScript-Teilbackup)
/// </summary>
public static class BackupLoader
{
    /// <summary>Aus dem Dateinamen geparstes Backup-Datum, z. B. …_2026_08_10-09_00_10_…</summary>
    private static readonly Regex DateInName =
        new(@"(\d{4})_(\d{2})_(\d{2})-(\d{2})_(\d{2})_(\d{2})", RegexOptions.Compiled,
            RegexLimits.MatchTimeout);

    /// <param name="log">
    /// Optionales Ladeprotokoll. Es wird aus <b>diesem</b> Thread beschrieben, damit die
    /// letzte Zeile auch dann auf der Platte steht, wenn das Programm anschließend nicht
    /// mehr reagiert. Siehe <see cref="LoadLog"/>.
    /// </param>
    public static Task<BackupData> LoadAsync(string path, IProgress<string>? progress = null,
                                             CancellationToken ct = default, LoadLog? log = null)
        => Task.Run(() => Load(path, progress, ct, log), ct);

    public static BackupData Load(string path, IProgress<string>? progress = null,
                                  CancellationToken ct = default, LoadLog? log = null)
    {
        if (!File.Exists(path))
            throw new NotABackupException($"Die Datei wurde nicht gefunden:\r\n{path}");

        var createdAt = ParseDateFromName(path) ?? File.GetLastWriteTime(path);
        log?.Step($"Datei geoeffnet ({LoadLog.Groesse(new FileInfo(path).Length)})");

        if (LooksLikeGzip(path))
            return LoadFromArchive(path, createdAt, compressed: true, progress, ct, log);

        // Ein Backitup-Archiv ohne gzip-Hülle ist kein Sonderfall, sondern auf macOS der
        // Regelfall: Safari und iCloud Drive packen .gz beim Herunterladen selbsttätig aus,
        // übrig bleibt ein reines .tar. Ohne diesen Zweig landete es beim JSON-Leser, der
        // dann über das erste Zeichen des Tar-Kopfes stolperte („'b' is an invalid start of
        // a value" — das b von „backup/…").
        if (LooksLikeTar(path))
            return LoadFromArchive(path, createdAt, compressed: false, progress, ct);

        return LoadFromLooseFile(path, createdAt, progress, ct);
    }

    // ---------------------------------------------------------------- Archiv

    internal static bool LooksLikeGzip(string path)
    {
        using var fs = File.OpenRead(path);
        return fs.ReadByte() == 0x1F && fs.ReadByte() == 0x8B;
    }

    /// <summary>
    /// true, wenn die Datei ein Tar-Archiv ist. Erkennbar an der Kennung „ustar" ab Byte 257
    /// des ersten Kopfblocks — sowohl POSIX- als auch GNU-Tar schreiben sie, und Backitup
    /// erzeugt eines davon. Der Dateiname bleibt außen vor, wie bei allen anderen Formaten
    /// auch.
    /// </summary>
    private static bool LooksLikeTar(string path)
    {
        using var fs = File.OpenRead(path);
        if (fs.Length < 512) return false;

        fs.Seek(257, SeekOrigin.Begin);
        Span<byte> magic = stackalloc byte[5];
        return fs.Read(magic) == 5 && magic.SequenceEqual("ustar"u8);
    }

    /// <summary>
    /// Liest das tar.gz im Stream. Da ein Tar nur vorwärts lesbar ist und wir mehrere
    /// Einträge brauchen (objects.jsonl, states.jsonl, vis-views.json), wird in einem
    /// einzigen Durchlauf alles Relevante eingesammelt. Nichts wird auf Platte entpackt.
    /// </summary>
    private static BackupData LoadFromArchive(string path, DateTime createdAt, bool compressed,
                                              IProgress<string>? progress, CancellationToken ct,
                                              LoadLog? log = null)
    {
        List<IobObject>? objects = null;
        List<ScriptInfo>? scripts = null;
        var visViews = new List<VisFile>();
        var files = new List<BackupFileInfo>();
        Dictionary<string, StateInfo>? states = null;
        var identity = new SystemIdentityReader();
        SystemIdentity? classicIdentity = null;
        var validation = new BackupValidation();
        var stateCount = 0;
        var skipped = 0;
        var isFull = false;

        using (var fs = File.OpenRead(path))
        // Ohne gzip-Huelle wird direkt aus der Datei gelesen; der Rest des Ablaufs ist
        // identisch, weil ein Tar in beiden Faellen nur vorwaerts gelesen wird.
        using (Stream source = compressed ? new GZipStream(fs, CompressionMode.Decompress) : fs)
        using (var tar = new TarReader(source))
        {
            TarEntry? entry;
            var geprueft = 0;

            // Wurde die jeweilige Pflichtdatei bereits OBEN im Archiv gefunden? Ein späterer
            // Fund weiter unten darf einen solchen nie überschreiben.
            var objectsOben = false;
            var statesOben = false;
            var scriptOben = false;
            var scriptQuelleGefunden = false;

            // Wo der bisher verwendete Fund lag — wird er später durch einen von oben
            // ersetzt, gehört er in die Liste der übergangenen Funde.
            string? objectsPfad = null;
            string? statesPfad = null;
            string? scriptPfad = null;

            /// <summary>
            /// Entscheidet, ob ein Fund verwendet wird.
            ///
            /// <b>Warum das nötig ist:</b> Erkannt wurde bisher allein am Dateinamen. Ein
            /// Archiv, das ein zweites Backup enthält — ein Adapter, der seine eigenen Daten
            /// mitsichert, ein versehentlich mitgepacktes Backup —, überschrieb damit
            /// stillschweigend die echte Objektliste; angezeigt wurde anschließend die
            /// falsche Anlage. Das macht keine Symptome, nur falsche Zahlen.
            ///
            /// <b>Warum nicht einfach alles unterhalb verwerfen:</b> Die Eintragsnamen in
            /// echten Archiven sind erstaunlich verschieden — manche tragen einen Vorspann,
            /// den der Tar-Leser mitliefert, andere legen die Datei ohne Ordner in die
            /// Wurzel. Ein zu strenger Filter würde ein Backup gar nicht mehr laden. Deshalb
            /// gilt: „oben" schlägt „unten", aber gibt es nichts oben, wird der Fund von
            /// unten weiterhin genommen.
            /// </summary>
            bool Annehmen(string pfad, string datei, bool schonVorhanden, ref bool schonOben,
                          ref string? bisher)
            {
                var oben = IsTopLevel(pfad, datei);

                if (!schonVorhanden)
                {
                    schonOben = oben;
                    bisher = pfad;
                    return true;
                }

                if (oben && !schonOben)
                {
                    // Der bisher genommene Fund lag weiter unten — er wird ersetzt, aber
                    // nicht verschwiegen.
                    if (bisher is not null) validation.IgnoredDuplicates.Add(CleanEntryPath(bisher));
                    schonOben = true;
                    bisher = pfad;
                    return true;
                }

                validation.IgnoredDuplicates.Add(CleanEntryPath(pfad));
                return false;
            }
            while ((entry = ReadNextEntrySafe(tar, out var readFailed)) is not null || readFailed)
            {
                ct.ThrowIfCancellationRequested();

                // Der Lesefehler wird beim Laden weiterhin toleriert (die bereits gelesenen
                // Einträge bleiben verwertbar), aber nicht mehr verschwiegen: Ein Archiv,
                // das mittendrin abbricht, ist unvollständig — und das muss im Urteil stehen.
                if (readFailed)
                {
                    validation.ArchiveTruncated = true;
                    break;
                }

                validation.EntriesRead++;
                var name = entry!.Name.Replace('\\', '/');
                log?.Detail($"Eintrag {validation.EntriesRead}: {LoadLog.Beschreibe(name, entry.Length)}");

                // Kenndaten aller Dateien aus dem files/-Baum einsammeln — im selben
                // Durchlauf, denn ein Tar lässt sich nur einmal vorwärts lesen. Der Inhalt
                // bleibt liegen; ihn holt erst der Export.
                if (DescribeFileEntry(name, entry) is { } fileInfo) files.Add(fileInfo);

                // 0-Byte-Einträge haben keinen Datenstrom. Für JSON-Dateien ist das kein
                // Grund zum Überspringen, sondern selbst ein Befund: Eine leere Datei ist
                // kein gültiges JSON — genau das meldet auch node („Unexpected end of JSON
                // input"). Für alles andere (Verzeichnisse, leere Bilder) ist es belanglos.
                if (entry.DataStream is null)
                {
                    if (IsFilesJson(name))
                        validation.OptionalFiles.Add(ValidateJson(CleanEntryPath(name), ""));
                    continue;
                }

                if (EndsWith(name, "objects.jsonl"))
                {
                    if (!Annehmen(name, "objects.jsonl", objects is not null, ref objectsOben,
                                  ref objectsPfad))
                        continue;

                    progress?.Report("Objekte werden gelesen …");
                    log?.Step("objects.jsonl wird gelesen");
                    validation.Objects.Present = true;
                    objects = ReadObjectsJsonl(entry.DataStream, identity, validation.Objects, out var s, ct);
                    skipped += s;
                    isFull = true;
                    log?.Step($"objects.jsonl fertig: {objects.Count:N0} Objekte, {s:N0} uebersprungen");
                }
                else if (EndsWith(name, "states.jsonl"))
                {
                    if (!Annehmen(name, "states.jsonl", states is not null, ref statesOben,
                                  ref statesPfad))
                        continue;

                    progress?.Report("States werden gelesen …");
                    log?.Step("states.jsonl wird gelesen");
                    validation.States.Present = true;
                    states = ReadStatesJsonl(entry.DataStream, validation.States, out stateCount, ct);
                    isFull = true;
                    log?.Step($"states.jsonl fertig: {stateCount:N0} Zeilen");
                }
                else if (EndsWith(name, "backup.json"))
                {
                    if (!Annehmen(name, "backup.json", objects is not null, ref objectsOben,
                                  ref objectsPfad))
                        continue;

                    // Ältere Backitup-Version: klassisches JSON-Objekt.
                    progress?.Report("backup.json wird gelesen …");
                    var res = ReadClassicJson(entry.DataStream, ct);
                    objects = res.Objects;
                    skipped += res.Skipped;
                    classicIdentity = res.Identity;
                    isFull = true;
                }
                else if (EndsWith(name, "script.json"))
                {
                    if (!Annehmen(name, "script.json", scriptQuelleGefunden, ref scriptOben,
                                  ref scriptPfad))
                        continue;
                    scriptQuelleGefunden = true;

                    progress?.Report("Skripte werden gelesen …");
                    var res = ReadClassicJson(entry.DataStream, ct);
                    // Ein Voll-Backup kann zusätzlich eine script.json enthalten; die
                    // Objektliste aus objects.jsonl hat dann Vorrang.
                    if (objects is null)
                    {
                        objects = res.Objects;
                        skipped += res.Skipped;
                        classicIdentity ??= res.Identity;
                    }
                    else
                    {
                        scripts = res.Objects.Where(o => o.Script is not null).Select(o => o.Script!).ToList();
                    }
                }
                else if (IsFilesJson(name))
                {
                    // Alle JSON-Dateien aus dem files/-Baum für die Backup-Prüfung strikt
                    // validieren (wie der js-controller: files/**/*.json optional).
                    //
                    // Der Inhalt wird dabei NICHT in den Speicher geholt: Die Prüfung braucht
                    // nur „gültig ja/nein" samt Fehlertext und wirft den Text sofort weg.
                    // Vorher wurde jede Datei komplett gepuffert — bei einer großen JSON eines
                    // fremden Adapters waren das mehrere Kopien der Datei im Arbeitsspeicher,
                    // und das Programm schien minutenlang zu stehen.
                    //
                    // Einzige Ausnahme ist die vis-views.json: Ihr Inhalt wird für die
                    // VIS-Auswertung gebraucht und deshalb einmal gelesen.
                    if (EndsWith(name, "vis-views.json"))
                    {
                        progress?.Report("VIS-Views werden gelesen …");
                        log?.Step($"vis-views.json wird gelesen ({LoadLog.Groesse(entry.Length)})");
                        var text = ReadAllTextDetectingBom(entry.DataStream, out var hasBom, ct);
                        validation.OptionalFiles.Add(ValidateJson(CleanEntryPath(name), text, hasBom));

                        visViews.Add(new VisFile
                        {
                            // vis-2.0 muss vor vis.0 geprüft werden — sonst matcht "vis." beides.
                            Version = name.Contains("/vis-2.", StringComparison.OrdinalIgnoreCase)
                                      || name.Contains("vis-2.0/", StringComparison.OrdinalIgnoreCase)
                                ? VisVersion.Vis2
                                : VisVersion.Vis1,
                            Path = CleanEntryPath(name),
                            Content = text
                        });
                    }
                    else
                    {
                        geprueft++;
                        if (geprueft % 250 == 0)
                        {
                            progress?.Report($"Dateien werden geprüft … ({geprueft:N0})");
                            log?.Step($"{geprueft:N0} JSON-Dateien geprueft");
                        }

                        validation.OptionalFiles.Add(
                            ValidateJsonStream(CleanEntryPath(name), entry.DataStream, ct));
                    }
                }
            }
        }

        if (objects is null)
            throw new NotABackupException(
                "Im Archiv wurde keine objects.jsonl, backup.json oder script.json gefunden.\r\n\r\n" +
                "Die Datei scheint kein ioBroker-Backup zu sein.");

        scripts ??= objects.Where(o => o.Script is not null).Select(o => o.Script!).ToList();

        // Aus dem JSONL-Pfad kommt der Sammler, aus dem klassischen Pfad ein fertiges
        // Ergebnis — je nachdem, welches Format im Archiv lag.
        var system = classicIdentity ?? identity.Build();

        // Die js-controller-Prüfung lief nur, wenn eine objects.jsonl vorlag (Voll-Backup).
        validation.WasChecked = validation.Objects.Present;
        validation.SkippedObjects = skipped;

        log?.Step($"Archiv durchlaufen: {validation.EntriesRead:N0} Eintraege, " +
                  $"{validation.OptionalCount:N0} JSON geprueft, {files.Count:N0} Dateien erfasst");

        progress?.Report("Backup wird ausgewertet …");
        var ergebnis = Build(path, createdAt, isFull, objects, scripts, visViews, stateCount, skipped,
                             states, system, validation, files, log);
        log?.Step($"Auswertung fertig: {ergebnis.Instances.Count:N0} Instanzen, " +
                  $"{ergebnis.Scripts.Count:N0} Skripte");
        return ergebnis;
    }

    /// <summary>true, wenn der Eintrag eine JSON-Datei im files/-Baum ist (nicht die JSONL).</summary>
    private static bool IsFilesJson(string name)
    {
        var n = name.Replace('\\', '/');
        return n.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
               && n.Contains("/files/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Beschreibt einen Archiveintrag als Datei des Admin-Dateibereichs — oder gibt null
    /// zurück, wenn er keine ist (Verzeichnis, Symlink, alles außerhalb von files/).
    /// </summary>
    private static BackupFileInfo? DescribeFileEntry(string name, TarEntry entry)
    {
        if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
            return null;

        var rest = FilesRelativePath(name);
        if (rest is null) return null;

        // Erster Ordner = Namensraum (0_userdata.0, vis-2.0 …), Rest = Pfad darin.
        var slash = rest.IndexOf('/');
        if (slash <= 0 || slash == rest.Length - 1) return null;

        return new BackupFileInfo
        {
            ArchivePath = CleanEntryPath(name),
            Namespace = rest[..slash],
            Path = rest[(slash + 1)..],
            Size = entry.Length
        };
    }

    /// <summary>
    /// Pfad unterhalb des Backitup-Dateibaums oder null. Maßstab ist ausschließlich
    /// <c>backup/files/</c>: Ein Adapter darf im Archiv einen eigenen Ordner mitbringen
    /// (energiefluss-erweitert legt unter <c>userFiles</c> ab), und dessen Inhalt gehört
    /// nicht in den Admin-Dateibereich.
    /// </summary>
    private static string? FilesRelativePath(string name)
    {
        var n = name.Replace('\\', '/');
        const string prefix = "backup/files/";

        var idx = n.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0) return n[(idx + prefix.Length)..];

        // Archive ohne führendes „backup/" (von Hand gepackt) kennen nur „files/".
        if (n.StartsWith("files/", StringComparison.OrdinalIgnoreCase)) return n["files/".Length..];
        if (n.StartsWith("./files/", StringComparison.OrdinalIgnoreCase)) return n["./files/".Length..];

        return null;
    }

    /// <summary>
    /// Prüft eine JSON-Datei strikt (wie der js-controller: reines JSON.parse, also sind
    /// //-Kommentare ungültig). Bei einem Fehler wird die Meldung samt Position festgehalten.
    /// </summary>
    /// <summary>
    /// Prüft eine JSON-Datei strikt. <paramref name="hasBom"/> wird als Fehler gewertet,
    /// obwohl .NET damit umgehen kann: Maßstab ist, was ioBroker tut — und dort scheitert
    /// <c>JSON.parse</c> an einem führenden Byte Order Mark („Unexpected token '﻿'").
    /// Ein BOM stillschweigend zu schlucken hieße, ein Backup für heil zu erklären, das
    /// Backitup beanstandet.
    /// </summary>
    private static JsonFileCheck ValidateJson(string path, string text, bool hasBom = false)
    {
        if (hasBom)
            return new JsonFileCheck
            {
                Path = path,
                Valid = false,
                Error = "Die Datei beginnt mit einem BOM (Byte Order Mark). " +
                        "JSON.parse in ioBroker lehnt das ab — die Datei muss ohne BOM " +
                        "als UTF-8 gespeichert werden."
            };

        try
        {
            using var doc = JsonDocument.Parse(text);
            return new JsonFileCheck { Path = path, Valid = true };
        }
        catch (JsonException ex)
        {
            return new JsonFileCheck { Path = path, Valid = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Ein defekter Tar-Eintrag darf nicht den gesamten Ladevorgang killen — die bereits
    /// gelesenen Einträge bleiben verwertbar. <paramref name="readFailed"/> unterscheidet
    /// dabei das reguläre Ende des Archivs von einem Abbruch mitten darin: Nur Letzteres
    /// bedeutet, dass Daten fehlen.
    /// </summary>
    private static TarEntry? ReadNextEntrySafe(TarReader tar, out bool readFailed)
    {
        readFailed = false;
        try
        {
            return tar.GetNextEntry();
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or FormatException)
        {
            readFailed = true;
            return null;
        }
    }

    /// <summary>
    /// Der Eintragspfad im Tar enthält Unterordner (backup/objects.jsonl), deshalb wird
    /// per EndsWith gematcht — aber nur auf Segmentgrenze, damit "meinobjects.jsonl"
    /// nicht fälschlich als "objects.jsonl" durchgeht.
    /// </summary>
    private static bool EndsWith(string path, string file)
    {
        if (!path.EndsWith(file, StringComparison.OrdinalIgnoreCase)) return false;
        var start = path.Length - file.Length;
        return start == 0 || path[start - 1] == '/';
    }

    /// <summary>
    /// Wie <see cref="EndsWith"/>, verlangt aber zusätzlich, dass die Datei <b>oben</b> im
    /// Archiv liegt: entweder ganz vorn oder unmittelbar in dessen Wurzelordner. Genau dort
    /// legt Backitup sie ab (<c>backup/objects.jsonl</c>).
    ///
    /// Für die vier Pflichtdateien ist das entscheidend: Ohne diese Bedingung gilt jede
    /// gleichnamige Datei irgendwo im Archiv als die echte — und die zuletzt gelesene
    /// gewinnt. Siehe <see cref="BackupValidation.IgnoredDuplicates"/>.
    /// </summary>
    private static bool IsTopLevel(string path, string file)
    {
        if (!EndsWith(path, file)) return false;

        // Beurteilt wird der bereinigte Pfad: Der Tar-Leser liefert die Eintragsnamen
        // mancher Archive mit einem Vorspann aus Leerzeichen und Zahlen aus — dort steht
        // der erste Schrägstrich mitten im Vorspann. Siehe CleanEntryPath.
        var clean = CleanEntryPath(path);

        // Entweder ohne Ordner in der Wurzel (so liegt script.json im Skript-Backup) …
        if (clean.Equals(file, StringComparison.OrdinalIgnoreCase)) return true;

        // … oder unmittelbar im Wurzelordner des Archivs (backup/objects.jsonl).
        return clean.Length == file.Length + 7
               && clean.StartsWith("backup/", StringComparison.OrdinalIgnoreCase)
               && clean.EndsWith(file, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Manche Einträge tragen ein Präfix aus PAX-Zeitstempeln im Namen
    /// ("15236273375 15236273374/backup/files/…"). Für die Anzeige wird ab dem
    /// eigentlichen backup/-Ordner gekürzt.
    /// </summary>
    internal static string CleanEntryPath(string name)
    {
        var idx = name.IndexOf("backup/", StringComparison.OrdinalIgnoreCase);
        return idx > 0 ? name[idx..] : name;
    }

    // ------------------------------------------------------- Lose Einzeldatei

    private static BackupData LoadFromLooseFile(string path, DateTime createdAt,
                                                IProgress<string>? progress, CancellationToken ct)
    {
        progress?.Report("Datei wird gelesen …");
        using var fs = File.OpenRead(path);

        // JSONL erkennen: erste nicht-leere Zeile ist ein vollständiges JSON-Objekt und
        // die Datei besteht aus mehreren solchen Zeilen.
        if (IsJsonLines(path))
        {
            var identity = new SystemIdentityReader();
            var validation = new BackupValidation();
            validation.Objects.Present = true;
            var objects = ReadObjectsJsonl(fs, identity, validation.Objects, out var skipped, ct);
            validation.WasChecked = true;
            validation.SkippedObjects = skipped;
            var scripts = objects.Where(o => o.Script is not null).Select(o => o.Script!).ToList();
            return Build(path, createdAt, true, objects, scripts, new List<VisFile>(), 0, skipped,
                         null, identity.Build(), validation);
        }

        var res = ReadClassicJson(fs, ct);
        if (res.Objects.Count == 0)
            throw new NotABackupException(
                "Die Datei enthält keine ioBroker-Objekte.\r\n\r\n" +
                "Erwartet wird eine script.json, backup.json oder objects.jsonl.");

        // Ohne Adapter-Instanzen ist es ein reines Skript-Backup.
        var isFull = res.Objects.Any(o => o.Type == "instance");
        var scr = res.Objects.Where(o => o.Script is not null).Select(o => o.Script!).ToList();
        return Build(path, createdAt, isFull, res.Objects, scr, new List<VisFile>(), 0, res.Skipped,
                     null, res.Identity);
    }

    private static bool IsJsonLines(string path)
    {
        using var sr = new StreamReader(path, Encoding.UTF8);
        var lines = 0;
        string? line;
        while (lines < 3 && (line = sr.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var t = line.TrimStart();
            // Ein pretty-printed JSON hat auf der ersten Zeile nur "{".
            if (t.Length < 3 || !t.StartsWith('{') || !line.TrimEnd().EndsWith('}')) return false;
            lines++;
        }
        return lines >= 2;
    }

    // ------------------------------------------------------------- Parser

    /// <summary>
    /// Liest objects.jsonl zeilenweise. Jede Zeile ist ein eigenständiges JSON-Objekt,
    /// daher bleibt der Speicherbedarf unabhängig von der Dateigröße konstant.
    /// </summary>
    private static List<IobObject> ReadObjectsJsonl(Stream stream, SystemIdentityReader identity,
                                                    JsonlCheck check, out int skipped, CancellationToken ct)
    {
        var result = new List<IobObject>(capacity: 20000);
        skipped = 0;

        using var sr = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        string? line;
        while ((line = sr.ReadLine()) is not null)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line)) continue;
            check.Lines++;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var obj = ObjectParser.Parse(doc.RootElement, null);
                if (obj is null) { skipped++; continue; }

                identity.Feed(doc.RootElement, obj.Id, obj.Type);
                result.Add(obj);
            }
            catch (JsonException ex)
            {
                // Für die Backup-Prüfung ist eine ungültige Zeile ein harter Fehler (wie im
                // js-controller); fürs Laden wird sie toleriert und übersprungen.
                skipped++;
                check.InvalidLines++;
                if (check.FirstErrorLine == 0) { check.FirstErrorLine = check.Lines; check.FirstError = ex.Message; }
            }
        }

        return result;
    }

    private sealed record ClassicResult(List<IobObject> Objects, int Skipped, SystemIdentity Identity);

    /// <summary>
    /// Liest script.json bzw. backup.json. Unterstützt beide Strukturvarianten aus
    /// Zwei Formen: IDs auf oberster Ebene (A) und gekapselt unter "objects" (B).
    /// </summary>
    private static ClassicResult ReadClassicJson(Stream stream, CancellationToken ct)
    {
        var objects = new List<IobObject>();
        var identity = new SystemIdentityReader();
        var skipped = 0;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(stream);
        }
        catch (JsonException ex)
        {
            throw new NotABackupException("Die Datei enthält kein gültiges JSON.\r\n\r\n" + ex.Message);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new NotABackupException("Die JSON-Datei enthält kein Objekt auf oberster Ebene.");

            // Variante B: alles unter "objects".
            if (root.TryGetProperty("objects", out var wrapped) && wrapped.ValueKind == JsonValueKind.Object)
                root = wrapped;

            foreach (var prop in root.EnumerateObject())
            {
                ct.ThrowIfCancellationRequested();

                // In backup.json stehen neben den Objekten auch Metadaten wie "config"
                // oder ein "states"-Abschnitt — die sind keine ioBroker-Objekte.
                if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                if (prop.Name is "config" or "states" or "meta") continue;

                var obj = ObjectParser.Parse(prop.Value, prop.Name);
                if (obj is null) { skipped++; continue; }

                identity.Feed(prop.Value, obj.Id, obj.Type);
                objects.Add(obj);
            }
        }

        return new ClassicResult(objects, skipped, identity.Build());
    }

    /// <summary>
    /// Liest states.jsonl zeilenweise und behält je State nur die Metadaten.
    ///
    /// Das Feld <c>val</c> wird nie übernommen: es enthält im realen Backup Binärdaten
    /// (ein Datenpunkt trägt ein komplettes JPEG als String). Die Zeile selbst muss zwar
    /// vollständig geparst werden, aber nichts davon bleibt im Speicher.
    ///
    /// Anders als in objects.jsonl heißt das ID-Feld hier <c>id</c> — ohne Unterstrich.
    /// </summary>
    private static Dictionary<string, StateInfo> ReadStatesJsonl(Stream stream, JsonlCheck check,
                                                                 out int lineCount, CancellationToken ct)
    {
        var result = new Dictionary<string, StateInfo>(capacity: 16000, StringComparer.Ordinal);
        lineCount = 0;

        using var sr = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        string? line;
        while ((line = sr.ReadLine()) is not null)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line)) continue;

            lineCount++;
            check.Lines++;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) continue;

                if (!root.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String)
                    continue;
                var id = idEl.GetString();
                if (string.IsNullOrEmpty(id)) continue;

                // Der eigentliche Zustand steckt unter "state"; ältere Formate legen die
                // Felder direkt auf oberster Ebene ab.
                var s = root.TryGetProperty("state", out var stateEl)
                        && stateEl.ValueKind == JsonValueKind.Object
                    ? stateEl : root;

                result[id] = new StateInfo
                {
                    Id = id,
                    Ts = ReadEpochMs(s, "ts"),
                    Lc = ReadEpochMs(s, "lc"),
                    Ack = !(s.TryGetProperty("ack", out var ack) && ack.ValueKind == JsonValueKind.False),
                    Quality = s.TryGetProperty("q", out var q) && q.ValueKind == JsonValueKind.Number
                              && q.TryGetInt32(out var qi) ? qi : 0,
                    From = s.TryGetProperty("from", out var f) && f.ValueKind == JsonValueKind.String
                        ? f.GetString() ?? "" : ""
                };
            }
            catch (JsonException ex)
            {
                // Einzelne kaputte Zeile überspringen — sie zählt trotzdem als State,
                // damit die Gesamtzahl der states.jsonl entspricht. Für die
                // Backup-Prüfung ist sie ein harter Fehler.
                check.InvalidLines++;
                if (check.FirstErrorLine == 0) { check.FirstErrorLine = lineCount; check.FirstError = ex.Message; }
            }
        }

        return result;
    }

    /// <summary>
    /// ioBroker-Zeitstempel sind Millisekunden seit Epoch. Manche Adapter schreiben sie
    /// als String — beide Formen werden akzeptiert, alles andere ergibt null.
    /// </summary>
    private static DateTime? ReadEpochMs(JsonElement el, string property)
    {
        if (!el.TryGetProperty(property, out var v)) return null;

        long ms;
        if (v.ValueKind == JsonValueKind.Number)
        {
            if (!v.TryGetInt64(out ms)) return null;
        }
        else if (v.ValueKind == JsonValueKind.String)
        {
            if (!long.TryParse(v.GetString(), out ms)) return null;
        }
        else return null;

        // 0 bedeutet „nie gesetzt"; Werte außerhalb des Gültigkeitsbereichs von
        // DateTimeOffset würden sonst eine Exception auslösen.
        if (ms <= 0 || ms > 253_402_300_799_999) return null;

        return DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;
    }

    /// <summary>
    /// Prüft einen JSON-Datenstrom, ohne ihn vollständig in den Speicher zu holen.
    ///
    /// <b>Warum nicht einfach <see cref="JsonDocument"/>?</b> Der braucht den ganzen Text.
    /// Eine JSON-Datei im dreistelligen Megabytebereich — Adapter legen im Dateibereich
    /// durchaus solche ab — belegte damit ein Vielfaches ihrer Größe an Arbeitsspeicher,
    /// nur um am Ende ein „gültig" zurückzugeben. <see cref="Utf8JsonReader"/> liest
    /// stattdessen häppchenweise mit festem Puffer.
    ///
    /// Die Regeln sind dieselben wie bei <see cref="ValidateJson"/> und damit dieselben wie
    /// im js-controller: Kommentare sind ungültig, ein BOM ebenfalls, eine leere Datei auch.
    /// Auch der Fehlertext ist derselbe — er kommt aus der gleichen
    /// <see cref="JsonException"/> und wird vom BackupCheckPresenter übersetzt.
    /// </summary>
    private static JsonFileCheck ValidateJsonStream(string path, Stream stream, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        var options = new JsonReaderOptions { CommentHandling = JsonCommentHandling.Disallow };
        var state = new JsonReaderState(options);

        var leftover = 0;
        var firstBlock = true;
        var tokens = 0L;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var read = stream.Read(buffer, leftover, buffer.Length - leftover);
            var length = leftover + read;
            var isFinal = read == 0;

            // Ein BOM verschluckt der StreamReader stillschweigend; ioBroker lehnt die Datei
            // deshalb ab, während sie hier als gültig durchginge. Die ersten drei Bytes
            // werden darum selbst angesehen.
            if (firstBlock)
            {
                firstBlock = false;
                if (length >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
                    return ValidateJson(path, "", hasBom: true);
            }

            var reader = new Utf8JsonReader(new ReadOnlySpan<byte>(buffer, 0, length), isFinal, state);

            try
            {
                while (reader.Read()) tokens++;
            }
            catch (JsonException ex)
            {
                return new JsonFileCheck { Path = path, Valid = false, Error = ex.Message };
            }

            state = reader.CurrentState;
            var consumed = (int)reader.BytesConsumed;
            leftover = length - consumed;

            if (leftover > 0) Buffer.BlockCopy(buffer, consumed, buffer, 0, leftover);
            if (isFinal) break;

            // Ein einzelnes Token, das nicht in den Puffer passt (eine sehr lange
            // Zeichenkette etwa), käme sonst nie voran — dann wächst der Puffer.
            if (leftover == buffer.Length) Array.Resize(ref buffer, buffer.Length * 2);
        }

        // Kein einziges Token heißt: leere Datei. Das ist auch für node kein gültiges JSON.
        // Die Meldung kommt bewusst aus derselben Quelle wie sonst, damit der Text im
        // Prüfbericht identisch bleibt.
        return tokens == 0 ? ValidateJson(path, "") : new JsonFileCheck { Path = path, Valid = true };
    }

    private static string ReadAllText(Stream stream, CancellationToken ct)
    {
        using var sr = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        return sr.ReadToEnd();
    }

    /// <summary>
    /// Wie <see cref="ReadAllText"/>, meldet aber zusätzlich ein führendes BOM.
    ///
    /// Nötig, weil <see cref="StreamReader"/> ein BOM standardmäßig verschluckt: Die
    /// Zeichenkette käme sauber heraus und die Datei würde als gültig durchgehen, obwohl
    /// ioBroker sie ablehnt. Deshalb werden die ersten drei Bytes vor dem Dekodieren
    /// selbst angesehen.
    /// </summary>
    private static string ReadAllTextDetectingBom(Stream stream, out bool hasBom, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var bytes = ms.ToArray();

        hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

        // Ohne BOM-Erkennung dekodieren — sonst verschwindet es hier wieder.
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(bytes);
    }

    // ------------------------------------------------------------- Aufbau

    private static BackupData Build(string path, DateTime createdAt, bool isFull,
                                    List<IobObject> objects, List<ScriptInfo> scripts,
                                    List<VisFile> visViews, int stateCount, int skipped,
                                    Dictionary<string, StateInfo>? states = null,
                                    SystemIdentity? system = null,
                                    BackupValidation? validation = null,
                                    List<BackupFileInfo>? files = null,
                                    LoadLog? log = null)
    {
        var instances = BuildInstances(objects);
        log?.Step("Instanzen gezaehlt");
        var adapterRefs = BuildAdapterRefs(objects, out var hasAdapterConfig);
        log?.Step("Adapter-Verweise gesammelt");
        AddAckHints(objects, scripts, instances, states);
        log?.Step("Skript-Befunde ergaenzt");

        return new BackupData
        {
            SourceFile = path,
            Kind = isFull ? BackupKind.Full : BackupKind.ScriptsOnly,
            CreatedAt = createdAt,
            Objects = objects,
            Scripts = scripts.OrderBy(s => s.DisplayPath, StringComparer.OrdinalIgnoreCase).ToList(),
            Instances = instances,
            AdapterRefs = adapterRefs,
            HasAdapterConfig = hasAdapterConfig,
            VisViews = visViews,
            Files = (files ?? new List<BackupFileInfo>())
                    .OrderBy(f => f.DisplayPath, StringComparer.OrdinalIgnoreCase).ToList(),
            StateCount = stateCount,
            States = states ?? new Dictionary<string, StateInfo>(StringComparer.Ordinal),
            System = system ?? new SystemIdentity(),
            SkippedCount = skipped,
            Validation = validation ?? new BackupValidation()
        };
    }

    /// <summary>
    /// Hängt den Skripten die Befunde zu „steuern/aktualisieren" an. Das geht erst hier: Ob
    /// ein eigener Datenpunkt wirklich unquittiert liegt, steht in states.jsonl, und wem ein
    /// Datenpunkt gehört, verrät erst der Instanzbestand.
    ///
    /// Bei Skript-Backups gibt es weder Instanzen noch States — dann ordnet
    /// <c>Besitzer</c> nichts zu, und es entsteht kein einziger Befund. Genau richtig: Ohne
    /// den Objektbestand wäre jede Aussage geraten.
    /// </summary>
    private static void AddAckHints(List<IobObject> objects, List<ScriptInfo> scripts,
                                    List<AdapterInstance> instances,
                                    Dictionary<string, StateInfo>? states)
    {
        var blockly = scripts.Where(s => !string.IsNullOrEmpty(s.BlocklyXml)).ToList();
        if (blockly.Count == 0) return;

        // Namensräume, hinter denen eine Adapter-Instanz steht. Alles andere ist entweder
        // selbst angelegt oder gar nicht zuzuordnen — geraten wird nicht.
        var adapterNs = new HashSet<string>(instances.Select(i => i.Namespace),
                                            StringComparer.OrdinalIgnoreCase);

        // Aliasse zeigen auf einen anderen Datenpunkt; beurteilt wird das Ziel. Ein Alias auf
        // eine Lampe ist ein Adapter-Datenpunkt, ein Alias auf 0_userdata ein eigener.
        var aliasZiel = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var o in objects)
            if (o.AliasTarget is { Length: > 0 } ziel && o.Id.StartsWith("alias.", StringComparison.Ordinal))
                aliasZiel[o.Id] = ziel;

        ScriptQualityAnalyzer.StateOwner Besitzer(string id)
        {
            // Höchstens einmal auflösen: Ein Alias auf einen Alias ist in ioBroker nicht
            // vorgesehen, und eine Schleife soll hier nicht entstehen können.
            if (id.StartsWith("alias.", StringComparison.Ordinal))
            {
                if (!aliasZiel.TryGetValue(id, out var ziel)) return ScriptQualityAnalyzer.StateOwner.Unknown;
                id = ziel;
                if (id.StartsWith("alias.", StringComparison.Ordinal))
                    return ScriptQualityAnalyzer.StateOwner.Unknown;
            }

            var erster = id.IndexOf('.');
            if (erster < 0) return ScriptQualityAnalyzer.StateOwner.Unknown;
            var zweiter = id.IndexOf('.', erster + 1);
            if (zweiter < 0) return ScriptQualityAnalyzer.StateOwner.Unknown;

            var ns = id[..zweiter];

            // Selbst angelegt: die beiden Ablagen, in denen kein Adapter quittiert.
            if (ns.StartsWith("0_userdata.", StringComparison.OrdinalIgnoreCase)
                || ns.StartsWith("javascript.", StringComparison.OrdinalIgnoreCase))
                return ScriptQualityAnalyzer.StateOwner.Own;

            return adapterNs.Contains(ns)
                ? ScriptQualityAnalyzer.StateOwner.Adapter
                : ScriptQualityAnalyzer.StateOwner.Unknown;
        }

        bool Unquittiert(string id)
        {
            // Beim Alias zählt der Zustand des Ziels: Der Alias selbst führt keinen eigenen.
            if (id.StartsWith("alias.", StringComparison.Ordinal)
                && aliasZiel.TryGetValue(id, out var ziel))
                id = ziel;

            return states is not null && states.TryGetValue(id, out var st) && !st.Ack;
        }

        // Erst alle Befehlskanäle einsammeln, dann erst urteilen: Quittiert wird ein
        // Datenpunkt oft von einem ganz anderen Skript als dem, das ihn beschreibt — ein
        // eigenes „ACK"-Skript, das reihum auf mehrere eigene Datenpunkte lauscht, ist ein
        // verbreitetes Muster. Wer nur das schreibende Skript ansieht, meldet genau das
        // als Fehler, was der Nutzer bewusst so gebaut hat.
        var befehlskanaele = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in blockly)
            foreach (var id in ScriptQualityAnalyzer.AcknowledgedStates(s.BlocklyXml))
                befehlskanaele.Add(id);

        bool Befehlskanal(string id)
        {
            if (befehlskanaele.Contains(id)) return true;

            // Auch über den Alias hinweg: Quittiert wird üblicherweise das Ziel.
            return id.StartsWith("alias.", StringComparison.Ordinal)
                   && aliasZiel.TryGetValue(id, out var ziel)
                   && befehlskanaele.Contains(ziel);
        }

        foreach (var s in blockly)
        {
            var weitere = ScriptQualityAnalyzer.AckHints(s.BlocklyXml, Besitzer, Unquittiert,
                                                        Befehlskanal);
            if (weitere.Count == 0) continue;

            s.Hints = s.Hints.Concat(weitere).OrderBy(h => h.Kind).ToList();
        }
    }

    /// <summary>
    /// Gleicht die ID-förmigen Zeichenketten aus den Adapter-Konfigurationen gegen den
    /// Objektbestand ab. Übrig bleibt nur, was wirklich ein Datenpunkt ist — alle übrigen
    /// Kandidaten (Hostnamen, Dateinamen, Zugangsdaten) werden hier verworfen und
    /// verlassen den Lader nie.
    /// </summary>
    /// <param name="hasAdapterConfig">
    /// false, wenn keine einzige Instanz einen gefüllten native-Abschnitt hatte. Dann ist
    /// „nichts gefunden" keine Aussage über die Anlage, sondern eine fehlende Quelle.
    /// </param>
    private static List<AdapterRef> BuildAdapterRefs(List<IobObject> objects, out bool hasAdapterConfig)
    {
        hasAdapterConfig = false;

        var states = new HashSet<string>(StringComparer.Ordinal);
        foreach (var o in objects)
            if (o.Type == "state") states.Add(o.Id);

        var result = new List<AdapterRef>();

        foreach (var o in objects)
        {
            if (o.NativeRefs is not { Count: > 0 }) continue;
            hasAdapterConfig = true;

            if (!o.Id.StartsWith("system.adapter.", StringComparison.Ordinal)) continue;
            var instance = o.Id["system.adapter.".Length..];

            // Je Instanz und Datenpunkt genügt der erste Fundort; ein Datenpunkt, der in
            // zwanzig Regeln derselben Instanz steht, soll die Liste nicht fluten.
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var candidate in o.NativeRefs)
            {
                if (!states.Contains(candidate.Value)) continue;
                if (!seen.Add(candidate.Value)) continue;

                result.Add(new AdapterRef
                {
                    Instance = instance,
                    Field = candidate.Field,
                    Label = candidate.Label,
                    StateId = candidate.Value,
                    InstanceEnabled = o.Enabled
                });
            }
        }

        return result
            .OrderBy(r => r.StateId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Instance, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Baut die Instanzliste aus system.adapter.&lt;name&gt;.&lt;nr&gt;-Objekten und zählt
    /// je Instanz die zugehörigen Objekte (Präfix &lt;name&gt;.&lt;nr&gt;.).
    /// </summary>
    private static List<AdapterInstance> BuildInstances(List<IobObject> objects)
    {
        var list = new List<AdapterInstance>();
        var byNamespace = new Dictionary<string, AdapterInstance>(StringComparer.OrdinalIgnoreCase);

        foreach (var o in objects)
        {
            if (o.Type != "instance") continue;
            if (!o.Id.StartsWith("system.adapter.", StringComparison.Ordinal)) continue;

            var rest = o.Id["system.adapter.".Length..];
            var dot = rest.LastIndexOf('.');
            if (dot <= 0) continue;

            var adapter = rest[..dot];
            if (!int.TryParse(rest[(dot + 1)..], out var nr)) continue;

            var inst = new AdapterInstance
            {
                Adapter = adapter,
                Instance = nr,
                Version = o.Version ?? "",
                Enabled = o.Enabled
            };
            list.Add(inst);
            byNamespace[inst.Namespace] = inst;
        }

        // Objektzählung je Instanz: ein Durchlauf über alle Objekte statt N Suchläufe.
        foreach (var o in objects)
        {
            var dot = o.Id.IndexOf('.');
            if (dot < 0) continue;
            var second = o.Id.IndexOf('.', dot + 1);
            if (second < 0) continue;

            var ns = o.Id[..second];
            if (byNamespace.TryGetValue(ns, out var inst)) inst.ObjectCount++;
        }

        // Objektlimit je Instanz. Ohne eigenes objectsWarnLimit-Objekt bleibt es bei der
        // Systemvorgabe, die AdapterInstance bereits mitbringt — so verhält sich auch der
        // js-controller, wenn der State fehlt.
        const string prefix = "system.adapter.";
        const string suffix = ".objectsWarnLimit";
        foreach (var o in objects)
        {
            if (o.ObjectsWarnLimit is not { } limit) continue;
            if (!o.Id.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (!o.Id.EndsWith(suffix, StringComparison.Ordinal)) continue;

            var ns = o.Id[prefix.Length..^suffix.Length];
            if (byNamespace.TryGetValue(ns, out var inst)) inst.ObjectLimit = limit;
        }

        return list
            .OrderBy(i => i.Adapter, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.Instance)
            .ToList();
    }

    private static DateTime? ParseDateFromName(string path)
    {
        Match m;
        try
        {
            m = DateInName.Match(Path.GetFileName(path));
        }
        catch (RegexMatchTimeoutException)
        {
            return null;   // ohne Datum weiterladen ist besser als gar nicht laden
        }

        if (!m.Success) return null;
        try
        {
            return new DateTime(
                int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value),
                int.Parse(m.Groups[4].Value), int.Parse(m.Groups[5].Value), int.Parse(m.Groups[6].Value));
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}

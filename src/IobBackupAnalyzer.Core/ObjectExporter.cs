using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace IobBackupAnalyzer.Core;

/// <summary>
/// Schreibt ausgewählte Objekte aus dem Backup als Datei, die der ioBroker-Admin unter
/// Objekte → Expertenmodus → „Objekte hochladen" importiert.
///
/// <b>Format</b> (vom Autor am echten Admin bestätigt, 27.11.2026): ein JSON-Objekt
/// <c>{"&lt;id&gt;": &lt;Objekt&gt;, …}</c>, jedes Objekt mit <c>_id</c> und allen Feldern
/// unverändert aus dem Backup. UTF-8 ohne BOM, eingerückt, Umlaute im Klartext.
///
/// <b>Warum ein zweiter Durchlauf über das Archiv?</b> Der Lader behält von jedem Objekt
/// nur die Felder, die eine Auswertung braucht (siehe <see cref="IobObject"/>). Für den
/// Import zählt aber das ganze Objekt, genau so, wie es im Backup steht — deshalb wird der
/// Eintrag, aus dem die Objektliste stammt (<see cref="BackupData.ObjectsEntry"/>), hier
/// noch einmal gelesen und die gewählten Objekte werden als <see cref="JsonElement"/>
/// unverändert weitergereicht. Nichts wird umgebaut: Ein Array mit einem Element bleibt ein
/// Array, eine Zahl bleibt eine Zahl.
///
/// <b>Was nie hineinkommt:</b> Werte aus <c>states.jsonl</c>. Gelesen wird ausschließlich
/// die Objektquelle.
///
/// Das Werkzeug schreibt damit weiterhin nichts in ein ioBroker-System — es erzeugt eine
/// Datei, die der Mensch selbst importiert.
/// </summary>
public static class ObjectExporter
{
    /// <param name="Json">Der Dateiinhalt.</param>
    /// <param name="Count">Zahl der geschriebenen Objekte.</param>
    /// <param name="Missing">Angefordert, in der Quelle aber nicht gefunden.</param>
    public sealed record Result(string Json, int Count, IReadOnlyList<string> Missing);

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        // Umlaute und Sonderzeichen bleiben lesbar statt als ü. Die Datei ist ein
        // Import für den Admin, kein HTML — die strengere Maskierung schützt hier nichts.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Erzeugt den Dateiinhalt für die angegebenen IDs. Reihenfolge in der Datei: nach ID.
    /// Steht eine ID mehrfach in der Quelle, gilt der letzte Eintrag.
    /// </summary>
    /// <exception cref="IOException">Die Backup-Datei ist nicht mehr lesbar.</exception>
    public static Result Build(BackupData data, IEnumerable<string> ids, CancellationToken ct = default)
    {
        var wanted = new HashSet<string>(ids, StringComparer.Ordinal);
        var found = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        if (wanted.Count > 0)
            ReadSource(data, wanted, found, ct);

        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, WriterOptions))
        {
            w.WriteStartObject();
            foreach (var id in found.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                                         .ThenBy(k => k, StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();
                w.WritePropertyName(id);
                WriteObject(w, id, found[id]);
            }
            w.WriteEndObject();
        }

        var missing = wanted.Where(id => !found.ContainsKey(id))
                            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
        return new Result(Encoding.UTF8.GetString(buffer.ToArray()), found.Count, missing);
    }

    /// <summary>
    /// Ein Objekt mit <c>_id</c>. Fehlt das Feld — in einer backup.json steht die ID nur als
    /// Schlüssel —, wird es vorangestellt, damit die Datei der bestätigten Form entspricht.
    /// </summary>
    private static void WriteObject(Utf8JsonWriter w, string id, JsonElement el)
    {
        if (el.TryGetProperty("_id", out _))
        {
            el.WriteTo(w);
            return;
        }

        w.WriteStartObject();
        w.WriteString("_id", id);
        foreach (var p in el.EnumerateObject()) p.WriteTo(w);
        w.WriteEndObject();
    }

    private static void ReadSource(BackupData data, HashSet<string> wanted,
                                   Dictionary<string, JsonElement> found, CancellationToken ct)
    {
        if (!File.Exists(data.SourceFile))
            throw new IOException($"Die Backup-Datei ist nicht mehr erreichbar:\n{data.SourceFile}");

        // Lose Datei: Sie selbst ist die Quelle.
        if (data.ObjectsEntry is null)
        {
            var jsonl = BackupLoader.IsJsonLines(data.SourceFile);
            using var fs = File.OpenRead(data.SourceFile);
            Collect(fs, jsonl, wanted, found, ct);
            return;
        }

        using (var fs = File.OpenRead(data.SourceFile))
        using (Stream source = BackupLoader.LooksLikeGzip(data.SourceFile)
                   ? new GZipStream(fs, CompressionMode.Decompress)
                   : fs)
        using (var tar = TarSource.Open(source))
        {
            TarItem? entry;
            while ((entry = tar.GetNextEntry()) is not null)
            {
                ct.ThrowIfCancellationRequested();
                if (entry.DataStream is null) continue;
                if (!string.Equals(entry.Name.Replace('\\', '/'), data.ObjectsEntry, StringComparison.Ordinal))
                    continue;

                var jsonl = data.ObjectsEntry.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase);
                Collect(entry.DataStream, jsonl, wanted, found, ct);
                return;
            }
        }

        throw new IOException($"Der Eintrag {data.ObjectsEntry} wurde im Archiv nicht wiedergefunden.");
    }

    private static void Collect(Stream stream, bool jsonl, HashSet<string> wanted,
                                Dictionary<string, JsonElement> found, CancellationToken ct)
    {
        if (jsonl)
        {
            using var sr = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            string? line;
            while ((line = sr.ReadLine()) is not null)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Schneller Vorfilter: Eine Zeile, deren Text keine gewünschte ID enthalten
                // kann, wird gar nicht erst geparst. Ohne ihn liefe bei einem Export von drei
                // Objekten der Parser über alle zwanzigtausend Zeilen.
                if (!line.Contains("\"_id\"", StringComparison.Ordinal)) continue;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object
                        || !root.TryGetProperty("_id", out var idEl)
                        || idEl.ValueKind != JsonValueKind.String)
                        continue;
                    var id = idEl.GetString()!;
                    if (wanted.Contains(id)) found[id] = root.Clone();   // letzter Eintrag gewinnt
                }
                catch (JsonException)
                {
                    // Wie beim Laden: Eine kaputte Zeile kostet nur sich selbst.
                }
            }
            return;
        }

        using var whole = JsonDocument.Parse(stream);
        var top = whole.RootElement;
        if (top.ValueKind != JsonValueKind.Object) return;
        if (top.TryGetProperty("objects", out var wrapped) && wrapped.ValueKind == JsonValueKind.Object)
            top = wrapped;

        foreach (var p in top.EnumerateObject())
        {
            ct.ThrowIfCancellationRequested();
            if (p.Value.ValueKind == JsonValueKind.Object && wanted.Contains(p.Name))
                found[p.Name] = p.Value.Clone();
        }
    }

    // ------------------------------------------------------------ Datei

    /// <summary>
    /// Vorschlag für den Dateinamen: <c>objekte_&lt;Backupdatum&gt;_&lt;Muster&gt;.json</c>.
    /// Aus dem Muster bleiben nur Zeichen, die jedes Dateisystem verträgt; <c>*</c> fällt weg.
    /// </summary>
    public static string FileName(BackupData data, string? pattern)
    {
        var datum = data.CreatedAt is { } d ? d.ToString("yyyy-MM-dd_HHmm") : "unbekannt";

        var sb = new StringBuilder();
        foreach (var c in pattern ?? "")
        {
            if (c is '*' or '?') continue;
            sb.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_');
        }
        var muster = sb.ToString().Trim('.', '_', '-');
        if (muster.Length > 60) muster = muster[..60].TrimEnd('.', '_', '-');
        if (muster.Length == 0) muster = "auswahl";

        return $"objekte_{datum}_{muster}.json";
    }

    /// <summary>
    /// Schreibt den Inhalt als UTF-8 ohne BOM. Erst in eine Nebendatei, am Ende umbenannt —
    /// ein abgebrochener Lauf hinterlässt kein Bruchstück, das wie ein fertiger Export
    /// aussieht. Ziel und Backup dürfen nicht dieselbe Datei sein.
    /// </summary>
    /// <exception cref="InvalidOperationException">Ziel ist die Backup-Datei.</exception>
    public static void WriteFile(string json, string target, string sourceFile)
    {
        var cmp = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (string.Equals(Path.GetFullPath(target), Path.GetFullPath(sourceFile), cmp))
            throw new InvalidOperationException(
                "Das Ziel ist die Backup-Datei selbst. Bitte einen anderen Dateinamen wählen.");

        var teil = target + ".teil";
        try
        {
            File.WriteAllText(teil, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(teil, target, overwrite: true);
        }
        catch
        {
            try { File.Delete(teil); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }
}

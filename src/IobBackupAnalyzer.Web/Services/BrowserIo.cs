using System.IO.Compression;
using System.Text;
using IobBackupAnalyzer.Core;
using Microsoft.JSInterop;

namespace IobBackupAnalyzer.Web.Services;

/// <summary>
/// Alles, was in den Desktop-Fassungen ein Datei- oder Speicherdialog erledigt.
///
/// Im Browser gibt es beides nicht: Eine Seite darf weder in einen Ordner schreiben noch
/// einen Pfad erfahren. Was dort ein Speicherdialog ist, ist hier ein Download; was dort
/// ein Zielordner für viele Dateien ist, wird hier zu einem ZIP mit denselben Dateien
/// darin. Die erzeugten Inhalte stammen dabei unverändert aus dem Core — dieselben
/// Exporter, die auch Windows, macOS und Linux benutzen.
/// </summary>
public sealed class BrowserIo
{
    private readonly IJSRuntime _js;

    /// <summary>
    /// Ordner im virtuellen Dateisystem der WebAssembly-Laufzeit.
    ///
    /// Das ist der Kunstgriff, der die ganze Portierung trägt: Der Core arbeitet
    /// durchgehend mit Dateipfaden — er öffnet das Archiv beim Laden, und er öffnet es
    /// beim Export ein zweites Mal, um eine einzelne Datei daraus zu holen. Ein
    /// Datenstrom allein hätte dafür nicht gereicht. Die hochgeladene Datei landet
    /// deshalb hier, und von da an ist für den Core alles wie auf einem Rechner.
    ///
    /// Der „Ordner" liegt im Arbeitsspeicher des Browser-Reiters und ist beim nächsten
    /// Aufruf der Seite wieder leer. Auf die Festplatte des Anwenders kommt nichts.
    /// </summary>
    public const string WorkDir = "/backups";

    public BrowserIo(IJSRuntime js) => _js = js;

    // ------------------------------------------------------------------ Hereinnehmen

    /// <summary>
    /// Legt eine hochgeladene Datei im virtuellen Dateisystem ab und liefert ihren Pfad.
    /// Eine zuvor abgelegte Datei gleichen Namens wird ersetzt.
    /// </summary>
    /// <param name="subFolder">
    /// Unterordner, falls die Datei neben einer gleichnamigen liegen muss — der Vergleich
    /// braucht das für das zweite Backup.
    ///
    /// Bewusst ein Ordner und kein Namenszusatz: Der Dateiname steht später in den
    /// Anzeigetexten („Vorher: …"), und dort hat eine Ablage-Eigenheit nichts verloren.
    /// </param>
    public static async Task<string> StoreAsync(Stream source, string fileName,
                                                string? subFolder = null,
                                                CancellationToken ct = default)
    {
        var dir = subFolder is null ? WorkDir : Path.Combine(WorkDir, subFolder);
        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, SafeName(fileName));
        if (File.Exists(path)) File.Delete(path);

        // 1 MB Blöcke: groß genug, dass das Kopieren nicht in kleinen Häppchen erstickt,
        // klein genug, dass der Puffer nicht selbst ins Gewicht fällt.
        await using var target = File.Create(path);
        await source.CopyToAsync(target, 1024 * 1024, ct);

        return path;
    }

    /// <summary>Räumt das virtuelle Dateisystem leer — vor jedem neuen Ladevorgang.</summary>
    public static void ClearWorkDir()
    {
        try
        {
            if (!Directory.Exists(WorkDir)) return;
            foreach (var file in Directory.GetFiles(WorkDir)) File.Delete(file);
        }
        catch (IOException)
        {
            // Ein nicht gelöschter Rest kostet Speicher, ist aber kein Grund, das Laden
            // abzubrechen.
        }
    }

    /// <summary>
    /// Ein Dateiname ohne Pfadanteile und ohne Zeichen, die im virtuellen Dateisystem
    /// stören. Der Name kommt vom Rechner des Anwenders und ist damit nichts, worauf man
    /// sich verlassen sollte.
    /// </summary>
    private static string SafeName(string name)
    {
        var bare = Path.GetFileName(name);
        if (string.IsNullOrWhiteSpace(bare)) return "backup.dat";

        var sb = new StringBuilder(bare.Length);
        foreach (var c in bare)
            sb.Append(char.IsControl(c) || c is '/' or '\\' or ':' ? '_' : c);

        return sb.ToString();
    }

    // ------------------------------------------------------------------ Herausgeben

    /// <summary>
    /// Reicht Text als Download weiter — CSV, Shell-Skript, Quelltext.
    /// </summary>
    /// <param name="bom">
    /// Byte-Order-Mark voranstellen. Für CSV ja, sonst zeigt Excel falsche Umlaute; für
    /// ein Shell-Skript auf keinen Fall, dort entwertete er die Shebang-Zeile. Deshalb
    /// steht die Vorgabe auf „nein" und die CSV muss es ausdrücklich verlangen.
    /// </param>
    public async Task SaveTextAsync(string fileName, string text,
                                    string mimeType = "text/plain", bool bom = false) =>
        await _js.InvokeVoidAsync("iobAnalyzer.speichernText", fileName, text, mimeType, bom);

    /// <summary>
    /// Reicht eine Datei aus dem virtuellen Dateisystem als Download weiter und löscht sie
    /// anschließend dort — sie wird nur zum Durchreichen erzeugt und würde sonst den
    /// Speicher des Reiters belegen.
    /// </summary>
    public async Task SaveVirtualFileAsync(string virtualPath, string downloadName,
                                           string mimeType = "application/octet-stream")
    {
        // Der Datenstrom muss offen bleiben, bis der Browser ihn ausgelesen hat — deshalb
        // erst nach dem Aufruf schließen und erst danach löschen.
        var stream = File.OpenRead(virtualPath);
        try
        {
            using var reference = new DotNetStreamReference(stream, leaveOpen: false);
            await _js.InvokeVoidAsync("iobAnalyzer.speichern", downloadName, reference, mimeType);
        }
        finally
        {
            try { File.Delete(virtualPath); } catch (IOException) { /* Rest bleibt liegen */ }
        }
    }

    /// <summary>
    /// Der Ersatz für „Zielordner wählen": Was die Desktop-Fassung als Ordner voller
    /// Dateien schreibt, wird hier zu einem ZIP mit demselben Inhalt.
    ///
    /// <paramref name="fill"/> bekommt einen leeren Ordner im virtuellen Dateisystem und
    /// füllt ihn mit genau dem Exporter, den auch die Desktop-Fassungen aufrufen. Danach
    /// wandert der Ordner in ein ZIP und der Browser bekommt es zum Herunterladen.
    /// </summary>
    public async Task SaveFolderAsZipAsync(string zipFileName, Action<string> fill)
    {
        var stage = Path.Combine(WorkDir, ".ausgabe");
        var zipPath = Path.Combine(WorkDir, ".ausgabe.zip");

        try
        {
            if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true);
            Directory.CreateDirectory(stage);
            if (File.Exists(zipPath)) File.Delete(zipPath);

            fill(stage);

            ZipFile.CreateFromDirectory(stage, zipPath, CompressionLevel.Optimal,
                                        includeBaseDirectory: false);

            await SaveVirtualFileAsync(zipPath, zipFileName, "application/zip");
        }
        finally
        {
            try
            {
                if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true);
            }
            catch (IOException)
            {
                // Der Zwischenordner liegt im Arbeitsspeicher und ist beim nächsten
                // Seitenaufruf ohnehin weg.
            }
        }
    }

    /// <summary>
    /// Eine CSV, wie sie die Desktop-Fassungen schreiben — Semikolon als Trenner, damit
    /// Excel sie ohne Rückfrage öffnet. Der Byte-Order-Mark kommt aus dem Javascript.
    /// </summary>
    public Task SaveCsvAsync(string fileName, IEnumerable<string> headers,
                             IEnumerable<string[]> rows) =>
        SaveTextAsync(fileName, ScriptExporter.CsvText(headers, rows), "text/csv", bom: true);

    // ------------------------------------------------------------------ Kleinkram

    public async Task<bool> CopyAsync(string text) =>
        await _js.InvokeAsync<bool>("iobAnalyzer.kopieren", text);

    /// <summary>
    /// Browser und Hauptversion — steht im Kopf des Ladeprotokolls. Ohne diese Angabe
    /// meldete es „Other 1.0.0.0": Im Browser kennt .NET sein Wirtssystem nicht.
    /// </summary>
    public async Task<string> BrowserKennungAsync()
    {
        try { return "WebAssembly · " + await _js.InvokeAsync<string>("iobAnalyzer.browserKennung"); }
        catch (JSException) { return "WebAssembly im Browser"; }
    }

    /// <summary>Belegter Speicher in MB — nur Chrome und Edge liefern das, sonst null.</summary>
    public async Task<int?> MemoryMbAsync()
    {
        try { return await _js.InvokeAsync<int?>("iobAnalyzer.speicherStand"); }
        catch (JSException) { return null; }
    }
}

using System.Globalization;
using System.Text;

namespace IobBackupAnalyzer.Core;

/// <summary>
/// Ein schlankes Ladeprotokoll — es beantwortet genau eine Frage: <b>Wo blieb es stehen?</b>
///
/// <b>Warum es das gibt.</b> Bleibt das Programm bei einem fremden Backup hängen, sieht man
/// von außen nur die zuletzt gesetzte Statuszeile — und die sagt wenig, weil sie schon
/// dastand, als der Schritt begann. Ohne Protokoll bleibt nur Raten. Mit ihm nennt die
/// letzte Zeile den Schritt, der nicht mehr fertig wurde.
///
/// <b>Zwei Eigenschaften sind dafür entscheidend:</b>
/// <list type="number">
/// <item>Jede Zeile wird <b>sofort</b> auf die Platte geschrieben. Ein Programm, das der
/// Nutzer im Task-Manager abschießt, hinterlässt sonst eine leere Datei — genau dann, wenn
/// sie gebraucht wird.</item>
/// <item>Geschrieben wird aus dem Thread, der gerade arbeitet. Ein Umweg über die
/// Oberfläche würde die letzte Zeile verschlucken, wenn es der UI-Thread ist, der hängt.</item>
/// </list>
///
/// <b>Was NICHT hineinkommt — verbindlich.</b> Das Protokoll ist zum Weitergeben gedacht,
/// deshalb enthält es ausschließlich Struktur und niemals Inhalt:
/// <list type="bullet">
/// <item>keine Objekt-IDs, keine Werte, keine Namen von Skripten, Ansichten oder Geräten</item>
/// <item>keine vollständigen Pfade aus dem Archiv — die tragen regelmäßig Raum- und
/// Gerätenamen. Von einer Datei erscheinen nur der Namensraum (also der Adapter, etwa
/// <c>vis-2.0</c>), die Endung und die Größe.</item>
/// <item>vom Backup selbst nur der Dateiname, nie der Pfad — der enthält den Benutzernamen</item>
/// </list>
/// Für die Fehlersuche reicht das: „hängt bei einer .json aus vis-2.0 mit 340 MB" ist die
/// Auskunft, auf die es ankommt.
/// </summary>
public sealed class LoadLog : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly object _sperre = new();
    private readonly DateTime _start = DateTime.Now;
    private int _zeilen;

    /// <summary>
    /// Ab so vielen Zeilen werden einzelne Archiv-Einträge nicht mehr aufgeführt. Die
    /// Phasen laufen weiter. Ein Archiv mit Zehntausenden Einträgen soll kein
    /// Hundert-Megabyte-Protokoll erzeugen.
    /// </summary>
    private const int MaxDetailZeilen = 20_000;

    private LoadLog(StreamWriter writer) => _writer = writer;

    /// <summary>Der Ablageort — dort, wo auch die Einstellungen liegen.</summary>
    public static string DefaultPath() => UserSettings.ResolveFilePath("ladeprotokoll.txt");

    /// <summary>
    /// Öffnet das Protokoll und schreibt den Kopf. Gibt <c>null</c> zurück, wenn das nicht
    /// geht — ein nicht beschreibbarer Ordner darf das Laden eines Backups nicht verhindern.
    /// </summary>
    /// <param name="systemBeschreibung">
    /// Womit das Programm gerade läuft. Ohne Angabe wird das Betriebssystem genommen —
    /// richtig auf einem Rechner, unbrauchbar im Browser: Dort meldet .NET „Other 1.0.0.0",
    /// weil es sein Wirtssystem gar nicht kennt. Wer daraus auf einen Defekt schließt,
    /// sucht an der falschen Stelle. Die Browser-Fassung reicht deshalb den Browsernamen
    /// herein — er ist das, was bei der Fehlersuche wirklich zählt.
    /// </param>
    public static LoadLog? Start(string path, string programmVersion, string backupDatei,
                                 string? systemBeschreibung = null)
    {
        try
        {
            var ordner = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(ordner)) Directory.CreateDirectory(ordner);

            var writer = new StreamWriter(path, append: false, Encoding.UTF8) { AutoFlush = true };
            var log = new LoadLog(writer);

            writer.WriteLine("ioBroker Backup Analyzer — Ladeprotokoll");
            writer.WriteLine("========================================");
            writer.WriteLine();
            writer.WriteLine("Dieses Protokoll enthaelt ausschliesslich Struktur: Schritte, Zeiten,");
            writer.WriteLine("Groessen und Adapter-Namensraeume. Es enthaelt KEINE Objekt-IDs, keine");
            writer.WriteLine("Werte, keine Namen von Skripten, Ansichten oder Geraeten und keine");
            writer.WriteLine("vollstaendigen Pfade. Es kann bedenkenlos weitergegeben werden.");
            writer.WriteLine();
            writer.WriteLine($"Programm : {programmVersion}");
            var kerne = Environment.ProcessorCount;
            writer.WriteLine($"System   : {systemBeschreibung ?? Environment.OSVersion.ToString()} / " +
                             $"{(Environment.Is64BitProcess ? "64" : "32")} Bit / " +
                             $"{kerne} {(kerne == 1 ? "Kern" : "Kerne")}");
            writer.WriteLine($"Backup   : {Path.GetFileName(backupDatei)}");
            writer.WriteLine($"Beginn   : {log._start:dd.MM.yyyy HH:mm:ss}");
            writer.WriteLine();
            writer.WriteLine("   Zeit  Speicher   Schritt");
            writer.WriteLine("-------  --------   ------------------------------------------------");

            return log;
        }
        catch (Exception)
        {
            // Kein Protokoll ist hinnehmbar; ein Absturz deswegen nicht.
            return null;
        }
    }

    /// <summary>
    /// Eine Zeile: verstrichene Zeit, belegter Speicher, Schritt. Der Speicher steht dabei,
    /// weil ein Stillstand oft keiner ist, sondern eine Anlage, die sich gerade
    /// dreistellige Megabyte am Stück holt.
    /// </summary>
    public void Step(string text)
    {
        try
        {
            lock (_sperre)
            {
                var sek = (DateTime.Now - _start).TotalSeconds;
                var mb = GC.GetTotalMemory(forceFullCollection: false) / 1048576.0;
                _writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,6:F2}s  {1,6:F0} MB   {2}", sek, mb, text));
                _zeilen++;
            }
        }
        catch (Exception)
        {
            // Ein voller Datenträger soll den Ladevorgang nicht abbrechen.
        }
    }

    /// <summary>
    /// Wie <see cref="Step"/>, aber nur solange das Protokoll nicht ausufert — für Zeilen,
    /// die je Archiv-Eintrag anfallen.
    /// </summary>
    public void Detail(string text)
    {
        if (_zeilen >= MaxDetailZeilen)
        {
            if (_zeilen == MaxDetailZeilen) { _zeilen++; Step("… weitere Einzelheiten werden nicht mehr protokolliert"); }
            return;
        }

        Step(text);
    }

    /// <summary>
    /// Beschreibt einen Archiv-Eintrag <b>ohne</b> seinen Pfad: Namensraum (der Adapter),
    /// Endung und Größe. Aus „files/vis-2.0/Wohnzimmer/ansicht.json" wird „vis-2.0 · .json ·
    /// 3,4 MB" — der Raumname bleibt draußen.
    /// </summary>
    public static string Beschreibe(string archivPfad, long groesse)
    {
        var p = archivPfad.Replace('\\', '/');

        var namensraum = "(Wurzel)";
        var idx = p.IndexOf("/files/", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var rest = p[(idx + 7)..];
            var slash = rest.IndexOf('/');
            namensraum = slash > 0 ? rest[..slash] : rest;
            if (namensraum.Length == 0) namensraum = "(files)";
        }
        else
        {
            // Ausserhalb von files/: der erste Ordner unterhalb der Archivwurzel. Das sind
            // Adapterordner wie „zigbee_0" — Adapternamen, keine Anlagendaten.
            var teile = p.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (teile.Length > 2) namensraum = teile[1];
        }

        var endung = Path.GetExtension(p);
        if (endung.Length == 0) endung = "(ohne Endung)";

        return $"{namensraum} · {endung} · {Groesse(groesse)}";
    }

    /// <summary>Größe in einer Form, die man lesen kann.</summary>
    public static string Groesse(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1048576 => string.Format(CultureInfo.InvariantCulture, "{0:F1} KB", bytes / 1024.0),
        _ => string.Format(CultureInfo.InvariantCulture, "{0:F1} MB", bytes / 1048576.0)
    };

    public void Dispose()
    {
        try
        {
            lock (_sperre)
            {
                _writer.WriteLine();
                _writer.WriteLine($"Ende: {DateTime.Now:HH:mm:ss}   " +
                                  $"Gesamtdauer: {(DateTime.Now - _start).TotalSeconds:F2} s");
                _writer.Dispose();
            }
        }
        catch (Exception)
        {
            // siehe Step
        }
    }
}

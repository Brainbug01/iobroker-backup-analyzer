using System.Formats.Tar;
using System.Text;

namespace IobBackupAnalyzer.Core;

/// <summary>Ein Eintrag im Archiv, unabhängig davon, welcher Leser ihn geliefert hat.</summary>
public sealed class TarItem
{
    public required string Name { get; init; }

    /// <summary>Größe der Nutzdaten in Bytes.</summary>
    public required long Length { get; init; }

    /// <summary>
    /// Die Nutzdaten — oder <c>null</c>, wenn der Eintrag keine hat: Verzeichnisse, Verweise
    /// und Dateien der Länge 0. Der Strom gilt nur bis zum nächsten
    /// <see cref="TarSource.GetNextEntry"/>; ein Tar lässt sich nur vorwärts lesen.
    /// </summary>
    public Stream? DataStream { get; init; }

    /// <summary>Eine gewöhnliche Datei — kein Verzeichnis, kein Verweis, kein Sondereintrag.</summary>
    public required bool IsRegularFile { get; init; }

    /// <summary>
    /// Zeitstempel aus dem Archiv. Der VIS-Export übernimmt ihn in die ZIP, damit man der
    /// entpackten Datei ansieht, von wann sie stammt.
    /// </summary>
    public DateTimeOffset ModificationTime { get; init; }
}

/// <summary>
/// Liest ein Tar-Archiv der Reihe nach durch — die eine Stelle, an der das ganze Programm
/// an Archive herangeht.
///
/// <b>Warum es diese Zwischenschicht gibt.</b> Auf dem Rechner erledigt
/// <see cref="TarReader"/> aus dem .NET-Umfang die Arbeit. Im Browser gibt es ihn nicht:
/// Für WebAssembly liefert .NET nur eine Attrappe von <c>System.Formats.Tar</c> aus, die
/// bei jedem Aufruf „System.Formats.Tar is not supported on this platform" wirft. Das ist
/// keine technische Grenze, sondern eine Voreinstellung zugunsten der Downloadgröße — und
/// da ein Backitup-Archiv genau ein Tar in einer Gzip-Hülle ist, wäre die Browser-Fassung
/// damit zwecklos.
///
/// Deshalb steht daneben ein eigener, kleiner Leser. Er springt nur dort ein, wo der
/// eingebaute fehlt; auf Windows, macOS und Linux arbeitet weiterhin unverändert
/// <see cref="TarReader"/>. Dass beide dasselbe liefern, prüft der Selbsttest an den echten
/// Testarchiven — Eintrag für Eintrag.
/// </summary>
public sealed class TarSource : IDisposable
{
    private readonly TarReader? _eingebaut;
    private readonly MinimalTarReader? _eigener;

    private TarSource(TarReader eingebaut) => _eingebaut = eingebaut;
    private TarSource(MinimalTarReader eigener) => _eigener = eigener;

    /// <summary>
    /// Ob der eingebaute Leser benutzt wird. Sichtbar für den Selbsttest, der beide
    /// Wege gegeneinander stellt.
    /// </summary>
    public static bool UsesBuiltIn => !OperatingSystem.IsBrowser();

    /// <summary>Öffnet den Strom zum Lesen. Der Strom wird nicht geschlossen.</summary>
    public static TarSource Open(Stream stream) =>
        UsesBuiltIn ? new TarSource(new TarReader(stream)) : new TarSource(new MinimalTarReader(stream));

    /// <summary>Ausdrücklich mit dem eigenen Leser — nur für den Selbsttest.</summary>
    public static TarSource OpenMinimal(Stream stream) => new(new MinimalTarReader(stream));

    /// <summary>
    /// Der nächste Eintrag, oder <c>null</c> am Ende des Archivs. Ein abgeschnittenes oder
    /// beschädigtes Archiv wirft — die Aufrufer fangen das und merken sich, dass das Archiv
    /// unvollständig war.
    /// </summary>
    public TarItem? GetNextEntry()
    {
        if (_eigener is not null) return _eigener.Next();

        var entry = _eingebaut!.GetNextEntry();
        if (entry is null) return null;

        return new TarItem
        {
            Name = entry.Name,
            Length = entry.Length,
            DataStream = entry.DataStream,
            IsRegularFile = entry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile,
            ModificationTime = entry.ModificationTime
        };
    }

    public void Dispose()
    {
        _eingebaut?.Dispose();
        _eigener?.Dispose();
    }
}

/// <summary>
/// Der eigene Tar-Leser für die Browser-Fassung.
///
/// Er kann genau so viel, wie ein Backitup-Archiv verlangt: gewöhnliche Dateien und
/// Verzeichnisse, ustar mit Namensvorspann, die langen Namen von GNU-Tar und die
/// PAX-Kopfsätze. Alles andere — Verweise, Gerätedateien, spärlich gespeicherte Dateien —
/// wird übersprungen, nicht nachgebildet.
///
/// Gelesen wird streng vorwärts und nie zurückgespult: Die Quelle ist im Regelfall ein
/// GZip-Strom, der das gar nicht könnte.
/// </summary>
internal sealed class MinimalTarReader : IDisposable
{
    private const int Blockgroesse = 512;

    private readonly Stream _quelle;
    private readonly byte[] _kopf = new byte[Blockgroesse];

    /// <summary>Der Teilstrom des zuletzt gelieferten Eintrags — er sperrt das Weiterlesen.</summary>
    private EintragStrom? _offen;

    /// <summary>Nutzdaten des laufenden Eintrags samt Auffüllung, soweit noch nicht gelesen.</summary>
    private long _restImBlock;

    public MinimalTarReader(Stream quelle) => _quelle = quelle;

    public TarItem? Next()
    {
        RestUeberspringen();

        // Ein Tar endet mit zwei Nullblöcken. In freier Wildbahn genügt schon einer, um
        // sicher zu sein, dass nichts Sinnvolles mehr kommt.
        string? langerName = null;

        while (true)
        {
            if (!BlockLesen()) return null;
            if (IstNullblock()) return null;

            PruefsummePruefen();

            var typ = (char)_kopf[156];
            var groesse = ZahlLesen(124, 12);

            // GNU-Tar legt einen zu langen Namen als eigenen Eintrag davor; PAX schreibt
            // ihn als Schlüssel-Wert-Satz. Beide gehören zum nachfolgenden Eintrag und
            // werden hier eingesammelt, statt nach außen gereicht zu werden.
            if (typ == 'L')
            {
                langerName = TextLesen(groesse).TrimEnd('\0');
                continue;
            }

            if (typ is 'x' or 'X' or 'g')
            {
                var ausPax = PfadAusPax(TextLesen(groesse));
                if (ausPax is not null) langerName = ausPax;
                continue;
            }

            // Langer Verweisname: gehört zu einem Verweis, den wir ohnehin überspringen.
            if (typ == 'K')
            {
                TextLesen(groesse);
                continue;
            }

            var name = langerName ?? NameAusKopf();
            var istDatei = typ is '0' or '\0' or '7';
            var zeit = ZeitLesen();

            _restImBlock = groesse + Auffuellung(groesse);

            // Nur gewöhnliche Dateien mit Inhalt bekommen einen Datenstrom — dasselbe
            // Verhalten wie beim eingebauten Leser, auf das sich die Aufrufer verlassen.
            if (istDatei && groesse > 0)
            {
                _offen = new EintragStrom(this, groesse);
                return new TarItem
                {
                    Name = name, Length = groesse, DataStream = _offen,
                    IsRegularFile = true, ModificationTime = zeit
                };
            }

            return new TarItem
            {
                Name = name, Length = groesse, DataStream = null,
                IsRegularFile = istDatei, ModificationTime = zeit
            };
        }
    }

    // ------------------------------------------------------------------ Kopf auswerten

    /// <summary>
    /// Der Name aus dem Kopfblock. Bei ustar steht ein etwaiger Vorspann getrennt davor —
    /// so tragen auch Pfade über 100 Zeichen.
    ///
    /// <b>Und genau hier liegt die Falle:</b> Die 155 Bytes ab Stelle 345 sind nur im
    /// echten ustar-Format der Vorspann. GNU-Tar schreibt an dieselbe Stelle Zugriffs- und
    /// Änderungszeit. Wer nicht hinsieht, hängt einem Verzeichnis „backup/" zwei
    /// Unix-Zeitstempel voran und wundert sich über Eintragsnamen wie
    /// „15236273373 15236273374/backup/". Aufgefallen ist das im Vergleichslauf des
    /// Selbsttests gegen den eingebauten Leser — an einem echten Backitup-Archiv.
    ///
    /// Unterschieden wird an der Kennung ab Stelle 257: „ustar\0" mit Version „00" ist
    /// POSIX, „ustar  " (mit Leerzeichen) ist GNU.
    /// </summary>
    private string NameAusKopf()
    {
        var name = TextAusKopf(0, 100);
        if (!IstPosixUstar()) return name;

        var vorspann = TextAusKopf(345, 155);
        return vorspann.Length == 0 ? name : vorspann.TrimEnd('/') + "/" + name;
    }

    /// <summary>Echtes ustar nach POSIX — nur dann gilt der Namensvorspann.</summary>
    private bool IstPosixUstar() =>
        _kopf[257] == 'u' && _kopf[258] == 's' && _kopf[259] == 't' &&
        _kopf[260] == 'a' && _kopf[261] == 'r' && _kopf[262] == 0;

    private string TextAusKopf(int von, int laenge)
    {
        var ende = von;
        var grenze = von + laenge;
        while (ende < grenze && _kopf[ende] != 0) ende++;
        return Encoding.UTF8.GetString(_kopf, von, ende - von);
    }

    /// <summary>
    /// Eine Zahl aus dem Kopfblock. Üblich ist oktal als Text; für Werte, die dort nicht
    /// hineinpassen, schreibt GNU-Tar sie als Binärzahl mit gesetztem höchsten Bit.
    /// </summary>
    private long ZahlLesen(int von, int laenge)
    {
        if ((_kopf[von] & 0x80) != 0)
        {
            long binaer = _kopf[von] & 0x7F;
            for (var i = von + 1; i < von + laenge; i++) binaer = (binaer << 8) | _kopf[i];
            return binaer;
        }

        long wert = 0;
        for (var i = von; i < von + laenge; i++)
        {
            var z = _kopf[i];
            if (z is 0 or (byte)' ') continue;
            if (z < '0' || z > '7') throw new InvalidDataException("Ungültige Zahl im Tar-Kopf.");
            wert = wert * 8 + (z - '0');
        }

        return wert;
    }

    /// <summary>
    /// Der Zeitstempel des Eintrags — Sekunden seit 1970, oktal im Kopf.
    ///
    /// Ein unlesbarer oder unsinniger Wert wird nicht zum Fehler: Der Zeitstempel ist
    /// Beiwerk, das nur der VIS-Export in die ZIP überträgt. Dort fängt die Prüfung auf
    /// Jahr ≥ 1980 den Ausfall ohnehin ab.
    /// </summary>
    private DateTimeOffset ZeitLesen()
    {
        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(ZahlLesen(136, 12));
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentOutOfRangeException)
        {
            return default;
        }
    }

    /// <summary>
    /// Die Prüfsumme des Kopfblocks. Sie ist die einzige Möglichkeit, einen abgeschnittenen
    /// oder verfälschten Kopf überhaupt zu bemerken — ohne sie liefe der Leser in
    /// Zufallsdaten weiter und meldete Einträge, die es nie gab.
    /// </summary>
    private void PruefsummePruefen()
    {
        var erwartet = ZahlLesen(148, 8);

        long alsVorzeichenlos = 0;
        long alsVorzeichenbehaftet = 0;

        for (var i = 0; i < Blockgroesse; i++)
        {
            // Das Prüfsummenfeld selbst zählt als Leerzeichen.
            var z = i is >= 148 and < 156 ? (byte)' ' : _kopf[i];
            alsVorzeichenlos += z;
            alsVorzeichenbehaftet += (sbyte)z;
        }

        // Einige alte Tar-Fassungen rechneten mit vorzeichenbehafteten Bytes. Beide
        // Lesarten gelten, sonst scheitert der Leser an Archiven, die sonst überall gehen.
        if (erwartet != alsVorzeichenlos && erwartet != alsVorzeichenbehaftet)
            throw new InvalidDataException("Falsche Prüfsumme im Tar-Kopf.");
    }

    /// <summary>Der Pfad aus einem PAX-Kopfsatz: „&lt;Länge&gt; path=&lt;Wert&gt;\n".</summary>
    private static string? PfadAusPax(string inhalt)
    {
        foreach (var zeile in inhalt.Split('\n'))
        {
            var trenner = zeile.IndexOf('=');
            if (trenner < 0) continue;

            var luecke = zeile.IndexOf(' ');
            if (luecke < 0 || luecke > trenner) continue;

            if (zeile[(luecke + 1)..trenner] == "path") return zeile[(trenner + 1)..];
        }

        return null;
    }

    // ------------------------------------------------------------------ Strom lesen

    private bool BlockLesen()
    {
        var gelesen = Fuellen(_kopf, 0, Blockgroesse);
        if (gelesen == 0) return false;
        if (gelesen < Blockgroesse) throw new EndOfStreamException("Tar-Kopf unvollständig.");
        return true;
    }

    private bool IstNullblock()
    {
        foreach (var z in _kopf)
            if (z != 0) return false;

        return true;
    }

    /// <summary>Der Inhalt eines Verwaltungseintrags — kurz genug, um ihn am Stück zu lesen.</summary>
    private string TextLesen(long groesse)
    {
        if (groesse is <= 0 or > 1024 * 1024)
        {
            Ueberspringen(groesse + Auffuellung(groesse));
            return "";
        }

        var puffer = new byte[groesse];
        if (Fuellen(puffer, 0, puffer.Length) < puffer.Length)
            throw new EndOfStreamException("Tar-Zusatzeintrag unvollständig.");

        Ueberspringen(Auffuellung(groesse));
        return Encoding.UTF8.GetString(puffer);
    }

    private static long Auffuellung(long groesse)
    {
        var rest = groesse % Blockgroesse;
        return rest == 0 ? 0 : Blockgroesse - rest;
    }

    /// <summary>
    /// Bringt den Strom an den nächsten Kopfblock — auch dann, wenn der Aufrufer die Daten
    /// des vorigen Eintrags gar nicht oder nur teilweise gelesen hat. Genau das ist der
    /// Regelfall: Von den meisten Einträgen interessieren nur die Kenndaten.
    /// </summary>
    private void RestUeberspringen()
    {
        if (_offen is not null)
        {
            _restImBlock -= _offen.Gelesen;
            _offen.Schliessen();
            _offen = null;
        }

        if (_restImBlock <= 0) { _restImBlock = 0; return; }

        Ueberspringen(_restImBlock);
        _restImBlock = 0;
    }

    private void Ueberspringen(long anzahl)
    {
        if (anzahl <= 0) return;

        // Bewusst lesen statt zu springen: Die Quelle ist meist ein GZip-Strom, und der
        // kann nicht springen.
        var puffer = new byte[Math.Min(anzahl, 64 * 1024)];
        var offen = anzahl;

        while (offen > 0)
        {
            var jetzt = (int)Math.Min(offen, puffer.Length);
            var gelesen = Fuellen(puffer, 0, jetzt);
            if (gelesen == 0) throw new EndOfStreamException("Tar-Archiv endet mitten im Eintrag.");
            offen -= gelesen;
        }
    }

    /// <summary>Liest so lange, bis der Puffer voll ist oder der Strom endet.</summary>
    private int Fuellen(byte[] puffer, int von, int anzahl)
    {
        var gesamt = 0;

        while (gesamt < anzahl)
        {
            var gelesen = _quelle.Read(puffer, von + gesamt, anzahl - gesamt);
            if (gelesen == 0) break;
            gesamt += gelesen;
        }

        return gesamt;
    }

    /// <summary>Für den Teilstrom: liest höchstens bis zum Ende des Eintrags.</summary>
    private int AusQuelle(byte[] puffer, int von, int anzahl) => Fuellen(puffer, von, anzahl);

    public void Dispose() => _offen?.Schliessen();

    /// <summary>
    /// Die Nutzdaten eines Eintrags als eigener Strom. Er endet an der im Kopf genannten
    /// Größe, damit der Aufrufer nicht versehentlich in den nächsten Eintrag hineinliest.
    /// </summary>
    private sealed class EintragStrom : Stream
    {
        private readonly MinimalTarReader _leser;
        private readonly long _laenge;
        private bool _zu;

        public EintragStrom(MinimalTarReader leser, long laenge)
        {
            _leser = leser;
            _laenge = laenge;
        }

        public long Gelesen { get; private set; }

        public void Schliessen() => _zu = true;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_zu) return 0;

            var offen = _laenge - Gelesen;
            if (offen <= 0) return 0;

            var jetzt = (int)Math.Min(count, offen);
            var gelesen = _leser.AusQuelle(buffer, offset, jetzt);
            Gelesen += gelesen;
            return gelesen;
        }

        public override bool CanRead => !_zu;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _laenge;

        public override long Position
        {
            get => Gelesen;
            set => throw new NotSupportedException();
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

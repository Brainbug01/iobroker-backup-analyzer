namespace IobBackupAnalyzer.Core;

/// <summary>Was in einem Archiv lag, das keine ioBroker-Objekte enthält.</summary>
public enum ArchivArt
{
    /// <summary>Nichts Bekanntes gefunden — es bleibt bei der allgemeinen Meldung.</summary>
    Unbekannt,
    Redis,
    Verlaeufe,
    SqlDatenbank,
    InfluxDb,
    Zigbee,
    Zigbee2Mqtt,
    NodeRed,

    /// <summary>
    /// Ein npm-Paketbaum statt Daten — das Archiv enthält ein Adapter-Installationsverzeichnis.
    /// </summary>
    Paketbaum
}

/// <summary>
/// Erkennt am <b>Inhalt</b> eines Archivs, was jemand geladen hat, wenn darin keine
/// ioBroker-Objekte lagen.
///
/// <b>Wozu das dient:</b> Backitup legt für jede aktivierte Sicherungsart eine eigene Datei an,
/// und alle enden auf <c>_backupiobroker.tar.gz</c>. Nur zwei davon enthalten Objekte. Wer eines
/// der übrigen lädt, bekam bisher „Die Datei scheint kein ioBroker-Backup zu sein" — formal
/// richtig, aber irreführend: Die Datei ist sehr wohl ein Backitup-Erzeugnis, nur eines ohne
/// Objekte. Statt zu rätseln soll dastehen, was man erwischt hat und was stattdessen zu laden ist.
///
/// <b>Warum nicht am Dateinamen:</b> Der Name wird an keiner Stelle des Programms zur Erkennung
/// herangezogen — gzip, tar und JSON werden alle an ihren ersten Bytes erkannt, nicht an ihrer
/// Endung. Diese Prüfung hält sich daran. Es kommt dazu, dass der Name einen frei wählbaren
/// Zusatz tragen kann, der in aller Regel ein Host- oder Personenname ist; er hat in keiner
/// Meldung etwas zu suchen.
///
/// <b>Woher die Merkmale stammen</b> (nachgesehen am 28.08.2026):
/// <list type="bullet">
///   <item><c>dump.rdb</c> — <c>simatec/ioBroker.backitup</c>, <c>32-redis.ts:187</c></item>
///   <item><c>*.sql</c> — ebenda, <c>30-mysql.ts:108</c> und <c>30-sqlite.ts:103</c></item>
///   <item><c>*.manifest</c> — ebenda, <c>12-influxDB.ts:140/142</c> (<c>influx backup</c>)</item>
///   <item><c>history.&lt;id&gt;.json</c> — <c>ioBroker.history</c>,
///         <c>src/lib/getHistory.ts:91</c>: <c>`${path}${date}/history.${getSafeId(id)}.json`</c></item>
///   <item><c>nvbackup.json</c>, <c>dev_names.json</c> — der Datenordner einer Zigbee-Instanz,
///         am Voll-Backup der Referenzanlage nachgesehen</item>
/// </list>
///
/// Zwei Merkmale stehen bewusst ohne Quellenangabe da: <c>flows.json</c> für Node-RED und
/// <c>configuration.yaml</c> für Zigbee2MQTT sind die üblichen Dateinamen dieser Projekte,
/// aber nicht im Backitup-Quelltext festgeschrieben — dort wird jeweils ein ganzer Ordner
/// eingepackt. Trifft eines davon nicht zu, fällt der Fall auf die allgemeine Meldung
/// zurück; behauptet wird dann nichts.
/// </summary>
public sealed class ArchivMerkmale
{
    public bool SahRdb { get; private set; }
    public bool SahVerlaufsdatei { get; private set; }
    public bool SahSqlDump { get; private set; }
    public bool SahManifest { get; private set; }
    public bool SahZigbeeDatei { get; private set; }
    public bool SahZigbee2MqttDatei { get; private set; }
    public bool SahFlows { get; private set; }
    public bool SahPaketbaum { get; private set; }

    /// <summary>
    /// Nimmt einen Eintragsnamen entgegen und merkt sich, was er verrät. Wird für jeden Eintrag
    /// im selben Durchlauf aufgerufen, in dem das Archiv ohnehin gelesen wird — der Inhalt bleibt
    /// dabei liegen, geprüft wird allein der Name des Eintrags.
    /// </summary>
    public void Betrachte(string? eintragsPfad)
    {
        if (string.IsNullOrEmpty(eintragsPfad)) return;

        var pfad = eintragsPfad.Replace('\\', '/');
        var datei = pfad[(pfad.LastIndexOf('/') + 1)..];
        if (datei.Length == 0) return;

        // Ein Programmverzeichnis erkennt man am Paketbaum — oder an seiner Beschreibung.
        // Beides zählt, weil node_modules nicht immer dabeiliegt: In einem Arbeitsbereich
        // (npm workspaces) steht der Ordner eine Ebene höher und wird nicht mit eingepackt,
        // die package.json des Pakets aber schon. Ohne die zweite Bedingung bliebe so ein
        // Archiv unerkannt, obwohl es denselben Fehler bezeugt.
        if (!SahPaketbaum
            && (pfad.Contains("/node_modules/", StringComparison.OrdinalIgnoreCase)
                || pfad.StartsWith("node_modules/", StringComparison.OrdinalIgnoreCase)
                || Gleich(datei, "package.json")
                || Gleich(datei, "package-lock.json")))
            SahPaketbaum = true;

        if (!SahRdb && datei.EndsWith(".rdb", StringComparison.OrdinalIgnoreCase))
            SahRdb = true;

        if (!SahSqlDump && datei.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            SahSqlDump = true;

        if (!SahManifest && datei.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase))
            SahManifest = true;

        // Der history-Adapter legt je Datenpunkt und Monat eine Datei „history.<id>.json" an.
        if (!SahVerlaufsdatei
            && datei.StartsWith("history.", StringComparison.OrdinalIgnoreCase)
            && datei.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            SahVerlaufsdatei = true;

        if (!SahZigbeeDatei
            && (Gleich(datei, "nvbackup.json") || Gleich(datei, "dev_names.json")))
            SahZigbeeDatei = true;

        if (!SahZigbee2MqttDatei && Gleich(datei, "configuration.yaml"))
            SahZigbee2MqttDatei = true;

        if (!SahFlows && Gleich(datei, "flows.json"))
            SahFlows = true;
    }

    private static bool Gleich(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Das Urteil. Die Reihenfolge ist kein Zufall: Merkmale echter Daten schlagen den
    /// Paketbaum, weil manche Sicherungen ihn zu Recht enthalten — ein Node-RED-Verzeichnis
    /// bringt sein <c>node_modules</c> mit. Erst wenn gar nichts auf Daten hindeutet, ist der
    /// Paketbaum selbst der Befund.
    /// </summary>
    public ArchivArt Art()
    {
        if (SahVerlaufsdatei) return ArchivArt.Verlaeufe;
        if (SahRdb) return ArchivArt.Redis;
        if (SahSqlDump) return ArchivArt.SqlDatenbank;
        if (SahManifest) return ArchivArt.InfluxDb;
        if (SahFlows) return ArchivArt.NodeRed;
        if (SahZigbeeDatei) return ArchivArt.Zigbee;
        if (SahZigbee2MqttDatei) return ArchivArt.Zigbee2Mqtt;
        if (SahPaketbaum) return ArchivArt.Paketbaum;
        return ArchivArt.Unbekannt;
    }

    /// <summary>
    /// Die vollständige Meldung, oder null, wenn nichts erkannt wurde. Null heißt: Es bleibt bei
    /// der allgemeinen Meldung, statt etwas zu behaupten.
    /// </summary>
    public string? Meldung() => Meldung(Art());

    /// <summary>Wie <see cref="Meldung()"/>, aber für eine bereits bestimmte Art.</summary>
    public static string? Meldung(ArchivArt art)
    {
        if (art == ArchivArt.Unbekannt) return null;

        if (art == ArchivArt.Paketbaum)
            return
                "Das Archiv enthält keine ioBroker-Objekte, sondern ein npm-Paketverzeichnis " +
                "(node_modules) — also ein Programmverzeichnis statt der Daten, die gesichert " +
                "werden sollten.\r\n\r\n" +
                "Das passiert in Backitup, wenn zu einer Sicherungsart der Quellpfad leer bleibt: " +
                "Aus dem leeren Feld wird das Arbeitsverzeichnis des Adapters, und gesichert wird " +
                "dessen Installationsordner. Die Sicherung enthält dann keine Nutzdaten und ist " +
                "beim Wiederherstellen wertlos.\r\n\r\n" +
                "Abhilfe: In der Backitup-Konfiguration den Pfad der betroffenen Sicherungsart " +
                "setzen oder dort „Detect config\" benutzen.";

        var inhalt = art switch
        {
            ArchivArt.Verlaeufe    => "die Verlaufsdaten des history-Adapters",
            ArchivArt.Redis        => "eine Redis-Datenbank",
            ArchivArt.SqlDatenbank => "den Abzug einer SQL-Datenbank",
            ArchivArt.InfluxDb     => "den Abzug einer InfluxDB",
            ArchivArt.NodeRed      => "die Node-RED-Konfiguration",
            ArchivArt.Zigbee       => "den Datenordner einer Zigbee-Instanz",
            ArchivArt.Zigbee2Mqtt  => "die Daten von Zigbee2MQTT",
            _                      => ""
        };

        return
            $"Das Archiv enthält keine ioBroker-Objekte, sondern {inhalt}.\r\n\r\n" +
            "Backitup legt für jede Sicherungsart eine eigene Datei an; nur das Voll-Backup " +
            "enthält Objekte und Werte. Für die Analyse wird deshalb die Datei gebraucht, deren " +
            "Name mit iobroker_ beginnt — oder javascripts_, wenn es nur um die Skripte geht.";
    }
}

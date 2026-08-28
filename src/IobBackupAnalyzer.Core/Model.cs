namespace IobBackupAnalyzer.Core;

/// <summary>Art der geladenen Quelle — steuert, welche Säulen im UI verfügbar sind.</summary>
public enum BackupKind
{
    /// <summary>Nur Skriptdaten (script.json / javascripts_*.tar.gz) — nur Säule 2.</summary>
    ScriptsOnly,
    /// <summary>Voll-Backup (objects.jsonl / backup.json) — alle drei Säulen.</summary>
    Full
}

public enum ScriptEngine
{
    Blockly,
    JavaScript,
    TypeScript,
    Unbekannt
}

/// <summary>
/// Ein ioBroker-Objekt, reduziert auf die für die Analysen benötigten Felder.
/// Der Rest der Rohdaten wird bewusst verworfen — bei 16.000+ Objekten spart das
/// erheblich Speicher (siehe STRUKTUR_VERIFIZIERUNG.md).
/// </summary>
public sealed class IobObject
{
    public string Id { get; init; } = "";
    public string Type { get; init; } = "";
    public string Name { get; init; } = "";

    /// <summary>Nur bei type=instance gesetzt (common.version).</summary>
    public string? Version { get; init; }

    /// <summary>
    /// Cron-Ausdruck aus common.restartSchedule: wann ioBroker diese Instanz planmäßig neu
    /// startet. Im Admin nur im Expertenmodus sichtbar und sonst nirgends auf einen Blick
    /// zu sehen. null, wenn kein Neustart geplant ist (der Regelfall).
    /// </summary>
    public string? RestartSchedule { get; init; }

    /// <summary>
    /// Protokollstufe der Instanz aus common.loglevel (debug/info/warn/error). null, wenn
    /// nicht gesetzt — dann gilt die Vorgabe des js-controllers, die im Backup nicht steht.
    /// Deshalb wird in dem Fall nichts behauptet.
    /// </summary>
    public string? LogLevel { get; init; }

    /// <summary>
    /// Betriebsart der Instanz aus common.mode. Siehe <see cref="AdapterInstance.Mode"/>
    /// für die Bedeutung und dafür, warum das Feld gebraucht wird.
    /// </summary>
    public string? Mode { get; init; }

    /// <summary>
    /// Cron-Ausdruck aus common.schedule: wann eine Instanz mit <c>mode=schedule</c> läuft.
    /// Nicht zu verwechseln mit <see cref="RestartSchedule"/> — das gilt nur für Dauerdienste.
    /// </summary>
    public string? Schedule { get; init; }

    /// <summary>
    /// common.dontDelete — das Objekt darf nicht gelöscht werden. Gilt laut Typdefinition
    /// für jeden Objekttyp, also auch für Datenpunkte. Solche Objekte bleiben aus den
    /// Aufräumlisten heraus (siehe <see cref="OrphanAnalyzer"/>).
    /// </summary>
    public bool DontDelete { get; init; }

    /// <summary>
    /// common.expert — im Admin nur bei eingeschaltetem Expertenmodus sichtbar. Wird in den
    /// Listen gekennzeichnet, damit niemand vergeblich danach sucht.
    /// </summary>
    public bool Expert { get; init; }

    /// <summary>Bei Skripten und Instanzen relevant. Fehlendes Feld gilt als aktiv.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>true, wenn common.custom vorhanden ist (History/InfluxDB/SQL-Logging).</summary>
    public bool HasCustom { get; init; }

    /// <summary>
    /// Nur bei type=instance: common.onlyWWW — die Instanz liefert ausschließlich Dateien
    /// aus und hat keinen eigenen Prozess. Siehe <see cref="AdapterInstance.OnlyWww"/>.
    /// </summary>
    public bool OnlyWww { get; init; }

    /// <summary>common.write: true = beschreibbar, false = nur lesend, null = nicht angegeben.</summary>
    public bool? Writable { get; init; }

    /// <summary>
    /// Nur bei <c>system.adapter.&lt;ns&gt;.objectsWarnLimit</c> gesetzt: der Vorgabewert aus
    /// common.def — ab welcher Objektzahl ioBroker die zugehörige Instanz beanstandet.
    /// </summary>
    public int? ObjectsWarnLimit { get; init; }

    /// <summary>
    /// Logging-Konfiguration je Instanz aus common.custom — eine Zeile je loggender
    /// Instanz (influxdb.0, history.1, sql.2 …), unabhängig von der Instanznummer.
    /// null, wenn kein Logging konfiguriert ist.
    /// </summary>
    public IReadOnlyList<CustomLoggingEntry>? CustomLogging { get; init; }

    /// <summary>Bei Alias-Objekten das Ziel aus common.alias.id (Lese-Ziel bevorzugt).</summary>
    public string? AliasTarget { get; init; }

    /// <summary>Bei Alias-Objekten das Lese-Ziel; bei String-Aliassen gleich dem Schreib-Ziel.</summary>
    public string? AliasRead { get; init; }

    /// <summary>Bei Alias-Objekten das Schreib-Ziel; bei String-Aliassen gleich dem Lese-Ziel.</summary>
    public string? AliasWrite { get; init; }

    /// <summary>
    /// Konvertierungsfunktion beim Lesen (common.alias.read) — JS-Code, der den Gerätewert
    /// in den Anzeigewert umrechnet. Nicht zu verwechseln mit dem Lese-<b>Ziel</b>
    /// (common.alias.id): das ist der Datenpunkt, dies nur die Umrechnung. null, wenn keine.
    /// </summary>
    public string? ConverterRead { get; init; }

    /// <summary>Konvertierungsfunktion beim Schreiben (common.alias.write). null, wenn keine.</summary>
    public string? ConverterWrite { get; init; }

    /// <summary>
    /// Wertetabelle aus common.states — Gerätewert → Anzeigelabel. Bei Aufzählungs-
    /// Datenpunkten (Thermostat-Modi, Schaltzustände …) gesetzt, sonst null. Grundlage
    /// für den Konverter-Generator.
    /// </summary>
    public IReadOnlyDictionary<string, string>? States { get; init; }

    /// <summary>Datentyp aus common.type (string/number/boolean …); null, wenn nicht gesetzt.</summary>
    public string? CommonType { get; init; }

    /// <summary>Einheit aus common.unit (°C, %, kWh …); null, wenn nicht angegeben.</summary>
    public string? Unit { get; init; }

    /// <summary>Rolle aus common.role (value.temperature, switch …); null, wenn nicht angegeben.</summary>
    public string? Role { get; init; }

    /// <summary>Untergrenze aus common.min als Text; null, wenn nicht angegeben.</summary>
    public string? Min { get; init; }

    /// <summary>Obergrenze aus common.max als Text; null, wenn nicht angegeben.</summary>
    public string? Max { get; init; }

    /// <summary>Vorgabewert aus common.def als Text; null, wenn nicht angegeben.</summary>
    public string? Default { get; init; }

    /// <summary>
    /// Von einem Chart-Objekt (type=chart) referenzierte Datenpunkt-IDs — je Chart-Linie
    /// das Feld id aus native.data.l, unabhängig von der Quell-Instanz (influxdb/history/
    /// sql/eigen). null bei allen anderen Objekten.
    /// </summary>
    public IReadOnlyList<string>? ChartRefs { get; init; }

    /// <summary>
    /// Bei Aufzählungen (<c>enum.rooms.*</c>, <c>enum.functions.*</c>) die zugeordneten
    /// Objekt-IDs aus common.members. null bei allen anderen Objekten; eine leere Liste
    /// heißt „Aufzählung ohne Mitglieder".
    /// </summary>
    public IReadOnlyList<string>? EnumMembers { get; init; }

    /// <summary>
    /// Bei type=instance die ID-förmigen Zeichenketten aus dem native-Abschnitt samt
    /// Feldpfad — Rohkandidaten, noch nicht gegen den Objektbestand geprüft. Der Loader
    /// wandelt sie am Ende in <see cref="BackupData.AdapterRefs"/> um und verwirft dabei
    /// alles, was kein bekannter Datenpunkt ist. null bei allen anderen Objekten.
    /// </summary>
    public IReadOnlyList<AdapterRefCandidate>? NativeRefs { get; init; }

    /// <summary>
    /// Nur bei einer Backitup-Instanz gesetzt, und auch dort nur, wenn deren native-Abschnitt
    /// die Einstellung wirklich mitbringt. null heißt „nicht bekannt" — nicht „ausgeschaltet".
    /// </summary>
    public HistoryBackupSetting? HistoryBackup { get; init; }

    /// <summary>Nur bei type=script gesetzt.</summary>
    public ScriptInfo? Script { get; init; }
}

/// <summary>
/// Die History-Sicherung einer Backitup-Instanz, auf das Nötigste eingedampft.
///
/// <b>Warum nur zwei Wahrheitswerte und nicht der Pfad selbst?</b> Für die Prüfung zählt
/// allein, ob überhaupt einer hinterlegt ist. Der Pfad selbst ist eine Angabe aus einer
/// fremden Anlage, die niemand braucht — und der native-Abschnitt ist genau der Ort, an dem
/// auch Passwörter und Hostnamen stehen. Was nicht gespeichert wird, kann in keiner Anzeige
/// und keinem Export auftauchen; dieselbe Linie zieht bereits
/// <see cref="IobObject.NativeRefs"/>.
/// </summary>
public sealed class HistoryBackupSetting
{
    /// <summary>Wert von <c>native.historyEnabled</c> — der Haken „History Backup".</summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// true, wenn <c>native.historyPath</c> eine nichtleere Zeichenkette ist. Ob der Pfad
    /// auf dem gesicherten Rechner auch existiert, ist von hier aus nicht feststellbar.
    /// </summary>
    public bool PathSet { get; init; }
}

/// <summary>
/// Eine noch ungeprüfte Zeichenkette aus einer Adapter-Konfiguration: sieht aus wie eine
/// Objekt-ID, muss aber keine sein.
/// </summary>
/// <param name="Value">Der gefundene Text.</param>
/// <param name="Field">Pfad im native-Baum, z. B. <c>customApps[0].objId</c>.</param>
/// <param name="Label">
/// Beschriftung des umgebenden Eintrags aus einem Namensfeld (etwa „power" bei einer
/// Anzeige-App); leer, wenn die Konfiguration dort keinen Namen führt.
/// </param>
public sealed record AdapterRefCandidate(string Value, string Field, string Label = "");

/// <summary>
/// Ein Datenpunkt, den eine Adapter-Instanz in ihrer eigenen Konfiguration nennt —
/// bestätigt gegen den Objektbestand.
/// </summary>
public sealed class AdapterRef
{
    /// <summary>Instanz in der Form <c>shuttercontrol.0</c>.</summary>
    public required string Instance { get; init; }

    /// <summary>Feldpfad im native-Abschnitt, z. B. <c>customApps[0].objId</c>.</summary>
    public required string Field { get; init; }

    /// <summary>
    /// Beschriftung des Eintrags aus der Adapter-Konfiguration, etwa der Name einer
    /// Anzeige-App. Leer, wenn dort kein Namensfeld steht.
    /// </summary>
    public string Label { get; init; } = "";

    /// <summary>Die referenzierte Datenpunkt-ID.</summary>
    public required string StateId { get; init; }

    /// <summary>false, wenn die Instanz im Backup abgeschaltet war.</summary>
    public bool InstanceEnabled { get; init; } = true;

    /// <summary>
    /// Fundstelle in lesbarer Form: erst der Name des Eintrags, dann der technische Pfad —
    /// „power (customApps[0].objId)". Ohne Namen bleibt der Pfad allein stehen, denn ein
    /// Feld wie <c>HomeLoadID</c> sagt für sich schon genug.
    /// </summary>
    public string Where => Label.Length == 0 ? Field : $"{Label}  ({Field})";
}

/// <summary>
/// Ein Logging-Eintrag aus common.custom: eine loggende Instanz samt ihren Optionen.
/// Die Instanz kann jeder Logging-Adapter in jeder Instanznummer sein — history.0,
/// influxdb.1, sql.2, sourceanalytix.0 …
/// </summary>
public sealed class CustomLoggingEntry
{
    /// <summary>Instanz-Schlüssel wie influxdb.0 oder history.1.</summary>
    public required string Instance { get; init; }

    /// <summary>false, wenn das Logging zwar konfiguriert, aber abgeschaltet ist.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Nur bei Wertänderung loggen statt in festem Intervall.</summary>
    public bool ChangesOnly { get; init; }

    /// <summary>Entprellzeit in Millisekunden (debounceTime, ersatzweise debounce); 0 = keine.</summary>
    public long DebounceMs { get; init; }

    /// <summary>Abweichender Name, unter dem die Instanz den Wert speichert; meist leer.</summary>
    public string AliasId { get; init; } = "";

    /// <summary>Adaptername ohne Instanznummer: influxdb.0 → influxdb.</summary>
    public string Adapter
    {
        get
        {
            var dot = Instance.LastIndexOf('.');
            return dot > 0 ? Instance[..dot] : Instance;
        }
    }
}

/// <summary>Ein Skript samt dekodiertem Blockly-XML.</summary>
public sealed class ScriptInfo
{
    /// <summary>Volle ioBroker-ID, z. B. script.js.Heizung.Nachtabsenkung</summary>
    public string Id { get; init; } = "";

    /// <summary>Letztes Segment der ID bzw. common.name.</summary>
    public string Name { get; init; } = "";

    /// <summary>
    /// Ordnerpfad ohne führendes script.js[.common|.global] und ohne den Skriptnamen,
    /// mit "/" getrennt. Leer für Skripte auf oberster Ebene.
    /// </summary>
    public string Folder { get; init; } = "";

    public ScriptEngine Engine { get; init; }
    public bool Enabled { get; init; } = true;

    /// <summary>Roher common.source inklusive Blockly-Kommentarblock.</summary>
    public string Source { get; init; } = "";

    /// <summary>Source ohne den angehängten Blockly-Base64-Kommentar.</summary>
    public string CleanSource { get; init; } = "";

    /// <summary>Dekodiertes und lesbar formatiertes Blockly-XML; null, wenn keins vorhanden.</summary>
    public string? BlocklyXml { get; init; }

    /// <summary>true, wenn engineType=Blockly ist, das XML aber nicht dekodiert werden konnte.</summary>
    public bool BlocklyBroken { get; init; }

    /// <summary>
    /// common.debug — die Schaltfläche „Debuggen" im Editor. <b>Kein Protokollschalter:</b>
    /// Der javascript-Adapter unterdrückt bei gesetztem Haken jede schreibende Operation
    /// (setState, exec, writeFile, setObject und weitere) und protokolliert sie nur als Warnung
    /// („was not executed, while debug mode is active"). Das Skript läuft also und tut nichts.
    /// </summary>
    public bool Debug { get; init; }

    /// <summary>
    /// common.verbose — „Ausführliche Protokollausgaben". Jede interne Operation des
    /// Sandkastens landet als info-Zeile im Protokoll.
    /// </summary>
    public bool Verbose { get; init; }

    /// <summary>
    /// Auffälligkeiten im Blockly-XML (siehe <see cref="ScriptQualityAnalyzer"/>). Immer leer
    /// bei JavaScript und TypeScript — dort gäbe es nur Textsuche und damit Fehltreffer.
    /// </summary>
    /// <remarks>
    /// Setzbar, weil ein Teil der Befunde erst feststeht, wenn das ganze Backup gelesen ist:
    /// Ob ein eigener Datenpunkt tatsächlich unquittiert liegt, verrät erst states.jsonl.
    /// Der Lader hängt diese Befunde in <c>Build</c> an (siehe <c>BackupLoader.AddAckHints</c>).
    /// </remarks>
    public IReadOnlyList<ScriptHint> Hints { get; set; } = Array.Empty<ScriptHint>();

    /// <summary>
    /// Die Hinweise in einer Zeile für die Listenspalte — gleiche Befunde zusammengefasst,
    /// damit ein Skript mit fünf abgelösten Bausteinen nicht die Spalte sprengt.
    /// </summary>
    public string HintsText => Hints.Count == 0
        ? ""
        : string.Join(", ", Hints.GroupBy(h => h.ShortText)
                                 .OrderBy(g => Hints.First(h => h.ShortText == g.Key).Kind)
                                 .Select(g => g.Count() == 1 ? g.Key : $"{g.Count()}× {g.Key}"));

    public string EngineText => Engine switch
    {
        ScriptEngine.Blockly => BlocklyBroken ? "Blockly (XML defekt)" : "Blockly",
        ScriptEngine.JavaScript => "JavaScript",
        ScriptEngine.TypeScript => "TypeScript",
        _ => "Unbekannt"
    };

    public string StatusText => Enabled ? "Aktiv" : "Deaktiviert";

    /// <summary>Voller Anzeigepfad, z. B. Heizung/Nachtabsenkung</summary>
    public string DisplayPath => string.IsNullOrEmpty(Folder) ? Name : Folder + "/" + Name;

    /// <summary>Durchsuchbarer Text für die Code-Suche: JS-Quelle plus Blockly-XML.</summary>
    public string SearchableCode => BlocklyXml is null ? CleanSource : CleanSource + "\n" + BlocklyXml;
}

/// <summary>
/// Ein State aus states.jsonl — die Metadaten und der zuletzt geschriebene Wert.
///
/// <b>Zur Vorgeschichte:</b> Der Wert (<c>val</c>) wurde bis v1.24.0 bewusst verworfen, weil
/// einzelne Datenpunkte im realen Backup Binärdaten tragen. Gemessen an der Referenzanlage
/// stimmt das Bild, trifft aber nur eine Handvoll Datenpunkte: von 13.724 States liegen
/// 65 über 1 KB und 17 über 10 KB; der größte (ein JSON-Gerätebaum) misst 380 KB, die
/// übrigen 99,5 % bleiben unter 1 KB. Nicht die Menge war das Problem, sondern die Ausreißer.
///
/// Deshalb wird der Wert jetzt geladen, aber bei <see cref="MaxValLength"/> Zeichen gekappt.
/// Das hält den Speicher bei der Größenordnung der Metadaten und kostet nur die Fähigkeit,
/// einen sehr großen Wert vollständig zu zeigen — <see cref="ValTruncated"/> weist darauf hin.
/// </summary>
public sealed class StateInfo
{
    /// <summary>
    /// Obergrenze des gespeicherten Werts in Zeichen.
    ///
    /// <b>Warum so großzügig:</b> Die erste Fassung kappte bei 4.096 Zeichen. Das genügt für
    /// eine Tabellenzelle, aber nicht, um einen Wert vollständig herauszuholen. Die
    /// Größenverteilung der Referenzanlage zeigt, wo die Grenze wehtut: 65 Werte liegen über
    /// 1 KB, 17 über 10 KB, darunter mehrere Gerätelisten zwischen 26 und 42 KB. Das sind
    /// durchweg JSON-Werte — also genau die, die man nicht abschreibt, sondern kopiert.
    ///
    /// Die Messung erlaubt die Großzügigkeit: Werte zu laden kostet keine messbare Ladezeit
    /// (1.464 ms ohne, 1.388–1.434 ms mit — der Unterschied liegt im Rauschen), weil die
    /// Zeile ohnehin geparst wird. Die Grenze schützt nur noch vor echten Ausreißern wie
    /// einem Kamerabild als Base64-Text.
    /// </summary>
    public const int MaxValLength = 65536;

    public required string Id { get; init; }

    /// <summary>
    /// Der zuletzt geschriebene Wert als Text. Zeichenketten stehen roh darin, alles andere
    /// (Zahl, Wahrheitswert, Objekt, Liste) in JSON-Schreibweise. Ein <c>null</c>-Wert und ein
    /// fehlendes Feld ergeben beide die leere Zeichenkette — <see cref="HasVal"/> trennt sie.
    /// </summary>
    public string Val { get; init; } = "";

    /// <summary>true, wenn das Feld <c>val</c> überhaupt vorhanden und nicht <c>null</c> war.</summary>
    public bool HasVal { get; init; }

    /// <summary>true, wenn der Wert länger war als <see cref="MaxValLength"/> und gekürzt wurde.</summary>
    public bool ValTruncated { get; init; }

    /// <summary>Ursprüngliche Länge des Werts in Zeichen — auch dann, wenn gekürzt wurde.</summary>
    public int ValLength { get; init; }

    /// <summary>Zeitpunkt des letzten Schreibvorgangs (auch ohne Wertänderung).</summary>
    public DateTime? Ts { get; init; }

    /// <summary>Zeitpunkt der letzten tatsächlichen Wertänderung (last change).</summary>
    public DateTime? Lc { get; init; }

    /// <summary>false = geschriebener Befehl wurde nie vom Adapter quittiert.</summary>
    public bool Ack { get; init; } = true;

    /// <summary>Qualitätscode. 0 = gut; alles andere meldet ein Problem der Quelle.</summary>
    public int Quality { get; init; }

    /// <summary>Wer geschrieben hat, z. B. system.adapter.javascript.0.</summary>
    public string From { get; init; } = "";

    /// <summary>Die aussagekräftigste Zeitangabe: Wertänderung, ersatzweise Schreibzeitpunkt.</summary>
    public DateTime? LastChange => Lc ?? Ts;

    /// <summary>Quelle ohne das Präfix system.adapter. — für die Anzeige.</summary>
    public string FromShort => From.StartsWith("system.adapter.", StringComparison.Ordinal)
        ? From["system.adapter.".Length..]
        : From;

    /// <summary>
    /// Meldet die Quelle ein Problem? Die unteren drei Bits des Qualitätscodes sind die
    /// Fehlerbits (allgemeines Problem, keine Verbindung, meldet Fehler); die oberen sagen
    /// nur, woher ein Ersatzwert stammt. <c>0x20</c> — der häufigste Code überhaupt — ist
    /// deshalb keine Störung, <c>0x44</c> dagegen schon.
    /// </summary>
    public bool QualityIsFault => (Quality & 0x07) != 0;

    /// <summary>
    /// Klartext des Qualitätscodes. Die Werte sind die ioBroker-Konstanten aus der
    /// Objekt-Schema-Doku des js-controllers; die Tabelle ist vollständig, ein Rest-Code
    /// wäre also ein Hinweis auf einen Adapter, der sich nicht an das Schema hält.
    ///
    /// Wichtig für die Einordnung: Nicht jeder Code ungleich 0 ist eine Störung. Die
    /// Ersatzwerte (0x10/0x20/0x40/0x80) sagen nur, dass der Wert nicht vom eigentlichen
    /// Erzeuger stammt — 0x20 etwa ist der Startwert, den ein Adapter beim Anlegen des
    /// Datenpunkts setzt und der nie durch einen echten Messwert ersetzt wurde.
    /// </summary>
    public string QualityText => Quality switch
    {
        0x00 => "gut",
        0x01 => "allgemeines Problem",
        0x02 => "keine Verbindung",
        0x10 => "Ersatzwert vom js-controller",
        0x11 => "Problem der Instanz",
        0x12 => "Instanz nicht verbunden",
        0x20 => "Startwert (nie echt beschrieben)",
        0x40 => "Ersatzwert von Gerät/Instanz",
        0x41 => "Problem des Geräts",
        0x42 => "Gerät nicht verbunden",
        0x44 => "Gerät meldet Fehler",
        0x80 => "Ersatzwert vom Sensor",
        0x81 => "Problem des Sensors",
        0x82 => "Sensor nicht verbunden",
        0x84 => "Sensor meldet Fehler",
        _ => $"unbekannter Code 0x{Quality:X2}"
    };

    /// <summary>Der Wert einzeilig für eine Tabellenzelle. Siehe <see cref="FormatVal"/>.</summary>
    public string ValText => FormatVal(Val, HasVal, ValTruncated, ValLength);

    /// <summary>
    /// Bereitet einen Wert für die Anzeige in einer Tabellenzeile auf.
    ///
    /// Zwei Eingriffe sind nötig, damit eine Zelle nicht die ganze Tabelle zerlegt: Zeilen-
    /// umbrüche und Tabulatoren werden zu Leerzeichen, und die Anzeige endet nach
    /// <see cref="DisplayLength"/> Zeichen. Ein gekürzter Wert bekommt die Originallänge
    /// angehängt, damit erkennbar bleibt, dass hier etwas fehlt — sonst läse sich ein
    /// abgeschnittenes JSON wie ein vollständiger Wert.
    /// </summary>
    public static string FormatVal(string val, bool hasVal, bool truncated, int length)
    {
        if (!hasVal) return "—";
        if (val.Length == 0) return truncated ? $"(leer, {length:N0} Zeichen)" : "";

        var flach = val;
        if (flach.AsSpan().IndexOfAny('\r', '\n', '\t') >= 0)
            flach = flach.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');

        if (flach.Length > DisplayLength)
            return $"{flach[..DisplayLength]}… ({length:N0} Zeichen)";

        return truncated ? $"{flach}… ({length:N0} Zeichen)" : flach;
    }

    /// <summary>
    /// Wie viele Zeichen eine Tabellenzelle höchstens zeigt. Deutlich kleiner als
    /// <see cref="MaxValLength"/>: Der gespeicherte Wert soll für die Zwischenablage taugen,
    /// die Zelle nur für den Blick.
    /// </summary>
    public const int DisplayLength = 120;
}

/// <summary>Eine Adapter-Instanz für die Inventar-Tabelle (Säule 1).</summary>
public sealed class AdapterInstance
{
    public string Adapter { get; init; } = "";
    public int Instance { get; init; }
    public string Version { get; init; } = "";
    public bool Enabled { get; init; }
    public int ObjectCount { get; set; }

    /// <summary>
    /// Geplanter Neustart als Cron-Ausdruck; leer, wenn keiner eingerichtet ist. Im Admin
    /// steht das nur im Expertenmodus.
    ///
    /// <b>Gilt nur für Dauerdienste.</b> Die Spezifikation sagt dazu wörtlich „CRON schedule
    /// to restart mode daemon adapters" — bei einer Instanz mit <c>mode=schedule</c> ist das
    /// Feld gegenstandslos, dort steht der Plan in <see cref="Schedule"/>.
    /// </summary>
    public string RestartSchedule { get; init; } = "";

    /// <summary>
    /// Ausführungsplan als Cron-Ausdruck bei <c>mode=schedule</c>; sonst leer.
    /// </summary>
    public string Schedule { get; init; } = "";

    /// <summary>
    /// Betriebsart aus common.mode. Zulässig sind <c>daemon</c>, <c>schedule</c>,
    /// <c>once</c>, <c>none</c> und <c>extension</c>; leer, wenn das Feld fehlt.
    ///
    /// <b>Warum das hier steht:</b> Nur ein <c>daemon</c> ist ein Dauerdienst, bei dem die
    /// Frage „läuft sie oder nicht" überhaupt sinnvoll ist. Eine Instanz mit <c>schedule</c>
    /// startet nach Plan und beendet sich wieder, <c>once</c> läuft einmal, <c>none</c> nie,
    /// und <c>extension</c> läuft im Prozess eines anderen Adapters. Ohne diese Angabe sähe
    /// eine abgeschaltete <c>once</c>-Instanz in der Tabelle aus wie eine vergessene — der
    /// gleiche Fehlschluss, den <see cref="OnlyWww"/> für die Datei-Adapter bereits verhindert.
    /// </summary>
    public string Mode { get; init; } = "";

    /// <summary>
    /// Die Betriebsart in Worten. Leer, wenn das Backup keine nennt: Dann ist die Vorgabe
    /// des js-controllers maßgeblich, und die steht hier nicht — also wird nichts behauptet.
    /// </summary>
    public string ModeText => Mode switch
    {
        "daemon" => "Dauerbetrieb",
        "schedule" => "nach Zeitplan",
        "once" => "einmalig",
        "none" => "startet nicht",
        "extension" => "in anderem Adapter",
        _ => Mode          // unbekannter Wert bleibt unverändert stehen
    };

    /// <summary>
    /// Der Zeitplan, der zur Betriebsart gehört: bei <c>schedule</c> der Ausführungsplan,
    /// sonst der geplante Neustart. Leer, wenn keiner hinterlegt ist.
    ///
    /// Beides in eine Spalte zu legen ist kein Zusammenwerfen von Ungleichem: Die Frage
    /// dahinter ist in beiden Fällen dieselbe — wann tut diese Instanz planmäßig etwas.
    /// Welcher Fall vorliegt, sagt die Spalte „Betriebsart" daneben.
    /// </summary>
    public string ScheduleText =>
        string.Equals(Mode, "schedule", StringComparison.Ordinal) ? Schedule : RestartSchedule;

    /// <summary>
    /// Protokollstufe der Instanz; leer, wenn nicht gesetzt. Leer heißt nicht „kein
    /// Protokoll", sondern „Vorgabe des js-controllers" — und die steht nicht im Backup.
    /// </summary>
    public string LogLevel { get; init; } = "";

    /// <summary>
    /// Die Protokollstufe so beschriftet, wie der Admin sie zur Auswahl stellt.
    ///
    /// Die fünf gültigen Werte stehen im js-controller, packages/types-dev/index.d.ts:
    /// <c>type LogLevel = 'silly' | 'debug' | 'info' | 'warn' | 'error';</c> — mehr gibt
    /// es nicht. Im Backup
    /// stehen die englischen Rohwerte; wer im Admin „Warnung" gewählt hat, soll hier nicht
    /// „warn" lesen müssen. Ein unbekannter Wert bleibt unverändert stehen, statt still
    /// zu verschwinden.
    /// </summary>
    public string LogLevelText => LogLevel switch
    {
        "" => "",
        "silly" => "Alles",
        "debug" => "Debug",
        "info" => "Info",
        "warn" => "Warnung",
        "error" => "Fehler",
        var anderes => anderes
    };

    /// <summary>
    /// Ab wie vielen Objekten der js-controller diese Instanz beanstandet. ioBroker legt den
    /// Wert je Instanz im State <c>system.adapter.&lt;ns&gt;.objectsWarnLimit</c> ab; die
    /// Vorgabe steht in <c>common.def</c> desselben Objekts und ist entweder der adaptereigene
    /// Wert (<c>common.defaultObjectsWarnLimit</c>) oder die Systemvorgabe
    /// <see cref="DefaultObjectLimit"/>.
    ///
    /// <b>Maßgeblich ist der Wert des States, nicht die Vorgabe.</b> Der js-controller liest
    /// ihn genau so: <c>typeof warnLimitState?.val === 'number' ? warnLimitState.val :
    /// DEFAULT_OBJECTS_WARN_LIMIT</c> (<c>packages/adapter/src/lib/adapter/adapter.ts</c>).
    /// Wer das Limit im Admin hochsetzt, ändert diesen Wert — bis v1.28.1 las der Analyzer
    /// nur die Vorgabe und zeigte deshalb eine Grenze an, die im laufenden System längst
    /// eine andere war. Fehlt der Wert oder ist er keine Zahl, gilt weiterhin die Vorgabe.
    /// </summary>
    public int ObjectLimit { get; set; } = DefaultObjectLimit;

    /// <summary>
    /// Systemvorgabe des js-controllers: <c>DEFAULT_OBJECTS_WARN_LIMIT</c> aus
    /// <c>packages/common-db/src/lib/common/constants.ts</c>.
    /// </summary>
    public const int DefaultObjectLimit = 5000;

    /// <summary>
    /// true, wenn ioBroker beim Start dieser Instanz die Meldung „This instance has N objects,
    /// the limit for this instance is set to M." schreibt und eine System-Meldung der Kategorie
    /// numberObjectsLimitExceeded anlegt. Verglichen wird echt größer — genau wie dort.
    /// </summary>
    public bool OverObjectLimit => ObjectCount > ObjectLimit;

    /// <summary>
    /// common.onlyWWW: Die Instanz hat keinen eigenen Prozess, sie liefert ausschließlich
    /// Dateien aus — VIS-Widget-Sätze sind der Regelfall.
    ///
    /// <b>Warum das hier stehen muss:</b> Bei solchen Instanzen sagt <see cref="Enabled"/>
    /// nichts über die Funktion. In der Referenzanlage steht <c>vis-timeandweather.0</c> auf
    /// <c>enabled=false</c> und bedient trotzdem 56 Widgets einwandfrei — die Dateien liegen
    /// im files-Bereich und werden unabhängig davon ausgeliefert. Ohne diese Unterscheidung
    /// führt die Übersicht sie als „Nein" und dämpft die Zeile grau; wer danach aufräumt,
    /// deinstalliert genau den Satz, den seine Ansichten brauchen.
    /// </summary>
    public bool OnlyWww { get; init; }

    public string Namespace => $"{Adapter}.{Instance}";

    /// <summary>
    /// „Ja"/„Nein" — außer bei <see cref="OnlyWww"/>: Dort ist die Frage nach dem Aktivsein
    /// gegenstandslos, und die Antwort wäre irreführend.
    /// </summary>
    public string EnabledText => OnlyWww ? "nur Dateien" : Enabled ? "Ja" : "Nein";

    /// <summary>
    /// Ob die Zeile in der Anzeige gedämpft wird. Eine Datei-Instanz ist nicht abgeschaltet,
    /// sie hat schlicht nichts zu starten.
    /// </summary>
    public bool Muted => !Enabled && !OnlyWww;
}

/// <summary>Ein Treffer aus Analyse A (Objekt-Leichen).</summary>
public sealed class OrphanObject
{
    public string Id { get; init; } = "";
    public string Type { get; init; } = "";
    public string Name { get; init; } = "";
    public string MissingInstance { get; init; } = "";

    /// <summary>common.expert — im Admin nur im Expertenmodus sichtbar.</summary>
    public bool Expert { get; init; }

    /// <summary>
    /// Der Objekttyp, bei einem Objekt im Expertenmodus mit dem Zusatz. Ohne ihn sucht der
    /// Nutzer den gemeldeten Eintrag im Admin und findet ihn nicht.
    /// </summary>
    public string TypeText => Expert ? $"{Type} · Expertenmodus" : Type;
}

/// <summary>Wie eine Datenpunkt-ID in Skripten/VIS gefunden wurde.</summary>
public enum FindKind
{
    /// <summary>Vollständige ID gefunden.</summary>
    Exakt,
    /// <summary>Nur ein Präfix der ID gefunden — die ID wird evtl. zusammengesetzt.</summary>
    NurPraefix,
    /// <summary>Nicht gefunden.</summary>
    Nicht
}

/// <summary>Ein Kandidat aus Analyse B (unbenutzte User-Datenpunkte).</summary>
public sealed class UnusedDatapoint
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public FindKind InScripts { get; init; }
    public FindKind InVis { get; init; }
    public bool AliasTarget { get; init; }
    public bool LoggingActive { get; init; }

    /// <summary>
    /// true, wenn der Datenpunkt in einer Chart-Definition (type=chart) referenziert wird —
    /// egal über welche Quell-Instanz (history/influxdb/sql/eigen). Ein so genutzter
    /// Datenpunkt ist kein Verwaisten-Kandidat.
    /// </summary>
    public bool InChart { get; init; }

    /// <summary>
    /// Letzte Wertänderung aus states.jsonl; null, wenn der Datenpunkt gar keinen State hat
    /// (angelegt, aber nie beschrieben).
    /// </summary>
    public DateTime? LastChange { get; init; }

    /// <summary>false = zu diesem Objekt existiert überhaupt kein State.</summary>
    public bool HasState { get; init; }

    /// <summary>Alter der letzten Wertänderung in Tagen, bezogen auf den Backup-Zeitpunkt.</summary>
    public int? AgeDays { get; init; }

    /// <summary>Der zuletzt geschriebene Wert. Siehe <see cref="StateInfo.Val"/>.</summary>
    public string Val { get; init; } = "";

    /// <summary>true, wenn überhaupt ein Wert vorhanden war.</summary>
    public bool HasVal { get; init; }

    /// <summary>true, wenn der Wert beim Laden gekürzt wurde.</summary>
    public bool ValTruncated { get; init; }

    /// <summary>Ursprüngliche Länge des Werts in Zeichen.</summary>
    public int ValLength { get; init; }

    /// <summary>Der Wert einzeilig für eine Tabellenzelle.</summary>
    public string ValText => StateInfo.FormatVal(Val, HasVal, ValTruncated, ValLength);

    /// <summary>Nur ein Kandidat, wenn keine der fünf Prüfungen angeschlagen hat.</summary>
    public bool IsCandidate =>
        InScripts == FindKind.Nicht && InVis == FindKind.Nicht && !AliasTarget
        && !LoggingActive && !InChart;

    /// <summary>
    /// Der Wert hat sich in den letzten 30 Tagen geändert — irgendetwas beschreibt den
    /// Datenpunkt also noch, auch wenn Skripte und VIS ihn nicht erwähnen. Solche Treffer
    /// sind die gefährlichsten Löschkandidaten und werden getrennt ausgewiesen.
    /// </summary>
    public bool RecentlyChanged => AgeDays is >= 0 and <= 30;

    /// <summary>
    /// Der Wert ist so frisch, dass der Datenpunkt sichtbar in Betrieb ist — er wird
    /// gerade beschrieben, nicht „vielleicht noch". Nur diese Treffer bleiben aus der
    /// Grundansicht von Analyse B heraus.
    ///
    /// <b>Warum sieben Tage und nicht dreißig:</b> <see cref="RecentlyChanged"/> beantwortet
    /// eine andere Frage — es warnt davor, dass ein Kandidat noch Leben zeigt, und färbt
    /// die Zeile entsprechend. Zum Ausblenden taugt diese Grenze nicht: Ein Datenpunkt, der
    /// zuletzt vor drei Wochen beschrieben wurde, ist ein legitimer Prüffall und gehört in
    /// die Liste. Ausgeblendet gehört nur, was offensichtlich läuft — etwa ein Skript, das
    /// seine IDs zur Laufzeit zusammensetzt und deshalb von keiner Textsuche gefunden wird.
    /// </summary>
    public bool JustWritten => AgeDays is >= 0 and <= 7;

    /// <summary>
    /// Kandidat, der zusätzlich seit über einem Jahr unverändert ist oder nie einen Wert
    /// hatte — die belastbarste Aussage, die das Backup über einen toten Datenpunkt zulässt.
    /// </summary>
    public bool IsStrongCandidate => IsCandidate && (!HasState || AgeDays is > 365);

    public static string FindText(FindKind k) => k switch
    {
        FindKind.Exakt => "Ja",
        FindKind.NurPraefix => "Nur Präfix",
        _ => "Nein"
    };

    public string InScriptsText => FindText(InScripts);
    public string InVisText => FindText(InVis);
    public string AliasText => AliasTarget ? "Ja" : "Nein";
    public string LoggingText => LoggingActive ? "Ja" : "Nein";
    public string InChartText => InChart ? "Ja" : "Nein";

    /// <summary>Letzte Wertänderung als Text — mit Alter, weil das die eigentliche Aussage ist.</summary>
    public string LastChangeText => !HasState
        ? "nie beschrieben"
        : LastChange is null ? "unbekannt"
        : AgeDays is null ? LastChange.Value.ToString("dd.MM.yyyy")
        : $"{LastChange.Value:dd.MM.yyyy}  ({AgeDays} T)";

    /// <summary>common.expert — im Admin nur im Expertenmodus sichtbar.</summary>
    public bool Expert { get; init; }

    /// <summary>
    /// Zusammenfassende Einstufung für die Ergebnisspalte.
    ///
    /// Bei einem Datenpunkt im Expertenmodus kommt der Zusatz dazu, und zwar bewusst hier
    /// statt in einer elften Spalte: Diese Spalte liest, wer gleich handeln will — und genau
    /// dann muss dastehen, dass der Eintrag im Admin ohne Expertenmodus unsichtbar ist.
    ///
    /// <b>Vorsorge, kein behobener Fehler.</b> In den geprüften Anlagen erreicht kein
    /// einziger solcher Datenpunkt diese Liste: Dort tragen das Kennzeichen durchweg
    /// Verwaltungsobjekte des JavaScript-Adapters (<c>scriptEnabled</c>,
    /// <c>scriptProblem</c>) — und die entfernt <see cref="OrphanAnalyzer"/> bereits über
    /// <c>JavascriptInternals</c>, aus einem ganz anderen Grund. Der Zusatz greift also
    /// erst, wenn ein Adapter einen eigenen Datenpunkt unter <c>0_userdata.0.*</c> so
    /// kennzeichnet.
    /// </summary>
    public string Verdict
    {
        get
        {
            var kern = !IsCandidate
                ? "verwendet"
                : RecentlyChanged ? "Kandidat — aber aktiv"
                : IsStrongCandidate ? "Kandidat — und tot"
                : "Kandidat";

            return Expert ? $"{kern} · Expertenmodus" : kern;
        }
    }
}

/// <summary>
/// Eine Zeile der Logging-Übersicht: ein Datenpunkt bei einer loggenden Instanz.
/// Ein Datenpunkt, der von mehreren Instanzen geloggt wird, ergibt mehrere Zeilen.
/// </summary>
public sealed class LoggingRow
{
    public required string Id { get; init; }
    public string Name { get; init; } = "";

    /// <summary>Loggende Instanz wie influxdb.0 oder history.1.</summary>
    public required string Instance { get; init; }

    /// <summary>Adaptername ohne Instanznummer.</summary>
    public string Adapter { get; init; } = "";

    public bool Enabled { get; init; } = true;
    public bool ChangesOnly { get; init; }
    public long DebounceMs { get; init; }
    public string AliasId { get; init; } = "";

    public string EnabledText => Enabled ? "Ja" : "Nein";
    public string ChangesOnlyText => ChangesOnly ? "Ja" : "Nein";
    public string DebounceText => DebounceMs > 0 ? $"{DebounceMs:N0} ms" : "";
}

/// <summary>
/// Ein installierter Adapter, zu dem keine eigene Instanz existiert.
///
/// Das ist eine Bestandsaufnahme, keine Löschliste: Manche Adapter laufen bewusst ohne
/// eigene Instanz — Socket-Backends wie ws/socketio (von admin/web bei entsprechender
/// Einstellung genutzt) oder reine Abhängigkeiten. Ein zuverlässiges Backup-Merkmal, das
/// „gebraucht" von „übrig" trennt, gibt es nicht (auch diese Adapter haben common.mode=
/// daemon). Host-gebundene system.host.*.adapter.*-Objekte sind ausgenommen.
/// </summary>
public sealed class AdapterWithoutInstance
{
    public required string Adapter { get; init; }
    public string Version { get; init; } = "";
}

/// <summary>
/// Eine Zeile der Alias-Übersicht: ein Alias mit seinem Lese- und Schreibziel und der
/// Angabe, ob diese Ziele im Backup noch existieren.
/// </summary>
public sealed class AliasRow
{
    public required string Id { get; init; }
    public string Name { get; init; } = "";

    /// <summary>Ziel des Lesezugriffs (common.alias.id bzw. dessen read).</summary>
    public string ReadTarget { get; init; } = "";

    /// <summary>Ziel des Schreibzugriffs; bei String-Aliassen gleich dem Lese-Ziel.</summary>
    public string WriteTarget { get; init; } = "";

    public bool ReadExists { get; init; }
    public bool WriteExists { get; init; }

    /// <summary>Konvertierungsfunktion beim Lesen (common.alias.read); leer, wenn keine.</summary>
    public string ConverterRead { get; init; } = "";

    /// <summary>Konvertierungsfunktion beim Schreiben (common.alias.write); leer, wenn keine.</summary>
    public string ConverterWrite { get; init; } = "";

    /// <summary>true, wenn der Alias überhaupt eine Konvertierungsfunktion verwendet.</summary>
    public bool HasConverter => ConverterRead.Length > 0 || ConverterWrite.Length > 0;

    public string ConverterText => HasConverter ? "Ja" : "Nein";

    /// <summary>Lese- und Schreibziel sind identisch — der Normalfall (String-Alias).</summary>
    public bool SingleTarget => WriteTarget.Length == 0 || string.Equals(WriteTarget, ReadTarget, StringComparison.Ordinal);

    /// <summary>Für die Anzeige: Schreibziel nur, wenn es vom Lese-Ziel abweicht.</summary>
    public string WriteTargetText => SingleTarget ? "" : WriteTarget;

    public string ReadExistsText => ReadTarget.Length == 0 ? "—" : ReadExists ? "Ja" : "FEHLT";
    public string WriteExistsText => SingleTarget ? "" : WriteTarget.Length == 0 ? "—" : WriteExists ? "Ja" : "FEHLT";

    /// <summary>Mindestens ein Ziel des Alias existiert nicht mehr im Backup.</summary>
    public bool Broken =>
        (ReadTarget.Length > 0 && !ReadExists) || (!SingleTarget && WriteTarget.Length > 0 && !WriteExists);
}

/// <summary>
/// Eine Zeile der States-Auswertung. Deckt beide Richtungen ab: einen State ohne Objekt
/// und ein Objekt ohne State — deshalb sind beide Seiten optional.
/// </summary>
public sealed class StateRow
{
    public required string Id { get; init; }

    /// <summary>Name aus dem Objekt; leer, wenn kein Objekt existiert.</summary>
    public string Name { get; init; } = "";

    /// <summary>Objekttyp (state, channel …); leer, wenn kein Objekt existiert.</summary>
    public string ObjectType { get; init; } = "";

    public bool HasObject { get; init; }
    public bool HasState { get; init; }

    public DateTime? LastChange { get; init; }

    /// <summary>Alter der letzten Wertänderung in Tagen, bezogen auf den Backup-Zeitpunkt.</summary>
    public int? AgeDays { get; init; }

    /// <summary>Schreibende Instanz ohne system.adapter.-Präfix.</summary>
    public string From { get; init; } = "";

    public int Quality { get; init; }
    public string QualityText { get; init; } = "";

    /// <summary>Der zuletzt geschriebene Wert. Siehe <see cref="StateInfo.Val"/>.</summary>
    public string Val { get; init; } = "";

    /// <summary>true, wenn überhaupt ein Wert vorhanden war.</summary>
    public bool HasVal { get; init; }

    /// <summary>true, wenn der Wert beim Laden gekürzt wurde.</summary>
    public bool ValTruncated { get; init; }

    /// <summary>Ursprüngliche Länge des Werts in Zeichen.</summary>
    public int ValLength { get; init; }

    /// <summary>Der Wert einzeilig für eine Tabellenzelle.</summary>
    public string ValText => StateInfo.FormatVal(Val, HasVal, ValTruncated, ValLength);

    /// <summary>
    /// Meldet die Quelle ein Problem? Siehe <see cref="StateInfo.QualityIsFault"/>: Nur die
    /// unteren drei Bits sind Fehlerbits — ein Ersatz- oder Startwert ist keine Störung.
    /// </summary>
    public bool QualityIsFault => (Quality & 0x07) != 0;

    /// <summary>common.write des Objekts: true = beschreibbar, false = nur lesend, null = unbekannt.</summary>
    public bool? Writable { get; init; }

    /// <summary>Anzeigetext dazu — leer, wenn das Objekt fehlt oder nichts angegeben ist.</summary>
    public string WritableText => Writable switch { true => "Ja", false => "Nein", _ => "" };
    public bool Ack { get; init; } = true;

    /// <summary>Namensraum (erste beide ID-Segmente) — Sortier- und Gruppierkriterium.</summary>
    public string Namespace
    {
        get
        {
            var first = Id.IndexOf('.');
            if (first < 0) return Id;
            var second = Id.IndexOf('.', first + 1);
            return second < 0 ? Id : Id[..second];
        }
    }

    public string LastChangeText => LastChange is null
        ? "—"
        : AgeDays is null ? LastChange.Value.ToString("dd.MM.yyyy HH:mm")
        : $"{LastChange.Value:dd.MM.yyyy HH:mm}  ({AgeDays} T)";

    public string AckText => Ack ? "Ja" : "Nein";
}

/// <summary>Ein Balken der Altersverteilung.</summary>
public sealed class AgeBucket
{
    public required string Label { get; init; }
    public int Count { get; set; }

    /// <summary>Untergrenze in Tagen — nur zur Einordnung beim Aufbau.</summary>
    public required int MinDays { get; init; }
}

/// <summary>Gesamtergebnis der States-Auswertung.</summary>
public sealed class StateReport
{
    /// <summary>States, zu denen kein Objekt mehr existiert — Werte-Leichen in der States-DB.</summary>
    public List<StateRow> StatesWithoutObject { get; init; } = new();

    /// <summary>state-Objekte, die nie einen Wert bekommen haben.</summary>
    public List<StateRow> ObjectsWithoutState { get; init; } = new();

    /// <summary>States mit Qualitätscode ungleich 0 — die Quelle meldet ein Problem.</summary>
    public List<StateRow> BadQuality { get; init; } = new();

    /// <summary>States mit ack=false — ein geschriebener Befehl wurde nie quittiert.</summary>
    public List<StateRow> Unacknowledged { get; init; } = new();

    /// <summary>Alle States, nach letzter Wertänderung absteigend im Alter.</summary>
    public List<StateRow> All { get; init; } = new();

    public List<AgeBucket> Ages { get; init; } = new();

    public int TotalStates { get; init; }
    public int TotalStateObjects { get; init; }

    /// <summary>
    /// Aliasse, die aus <see cref="ObjectsWithoutState"/> herausgehalten wurden. Sie haben
    /// nie einen eigenen Wert in der States-DB — gelesen und geschrieben wird das Ziel aus
    /// <c>common.alias.id</c>. Die Zahl steht in der Kennzeilen-Anzeige, damit die
    /// Auslassung sichtbar bleibt und nicht wie ein Zählfehler aussieht.
    /// </summary>
    public int AliasesWithoutOwnState { get; init; }

    /// <summary>true, wenn das Backup überhaupt eine states.jsonl enthielt.</summary>
    public bool HasStates => TotalStates > 0;
}

/// <summary>Welche VIS-Version eine View-Datei stammt.</summary>
public enum VisVersion
{
    /// <summary>vis.0 — das klassische VIS.</summary>
    Vis1,
    /// <summary>vis-2.0 — VIS 2.</summary>
    Vis2
}

/// <summary>Eine vis-views.json aus dem Backup samt Herkunft.</summary>
public sealed class VisFile
{
    public required VisVersion Version { get; init; }
    /// <summary>Pfad im Archiv, z. B. backup/files/vis-2.0/main/vis-views.json</summary>
    public required string Path { get; init; }
    public required string Content { get; init; }

    /// <summary>
    /// Name des VIS-Projekts — der Ordner vor der Datei, im Regelfall „main".
    ///
    /// Eine ioBroker-Installation kann mehrere Projekte je VIS-Version haben; auf einer
    /// anderen Anlage lagen drei nebeneinander. Ohne diesen Namen wären
    /// gleichnamige Views verschiedener Projekte nicht auseinanderzuhalten — bei kopierten
    /// Projekten ist Gleichnamigkeit sogar der Normalfall.
    /// </summary>
    public string Project
    {
        get
        {
            var parts = Path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            // …/files/<vis-Instanz>/<Projekt>/vis-views.json — das Projekt steht direkt davor.
            return parts.Length >= 2 ? parts[^2] : "";
        }
    }

    public string VersionText => Version == VisVersion.Vis1 ? "VIS 1" : "VIS 2";
}

/// <summary>Grobe Einordnung einer Datei nach ihrer Endung — nur für Anzeige und Filter.</summary>
public enum BackupFileKind
{
    Sonstiges,
    Bild,
    Text,
    Audio,
    Video,
    Archiv
}

/// <summary>
/// Eine Datei aus dem <c>files/</c>-Baum des Backups — das, was der Admin unter „Dateien"
/// zeigt. Erfasst werden nur die Kenndaten; der Inhalt bleibt im Archiv und wird erst beim
/// Export nachgelesen (<see cref="BackupFileExporter"/>). Ein Backup mit Kamerabildern
/// brächte sonst zweistellige Megabytes in den Arbeitsspeicher, ohne dass sie je jemand
/// ansieht.
/// </summary>
public sealed class BackupFileInfo
{
    /// <summary>Voller Pfad im Archiv, z. B. backup/files/vis-2.0/main/vis-views.json</summary>
    public required string ArchivePath { get; init; }

    /// <summary>Namensraum, also der erste Ordner unter files/ — z. B. 0_userdata.0.</summary>
    public required string Namespace { get; init; }

    /// <summary>Pfad innerhalb des Namensraums, z. B. bilder/aufnahme-01.08.2026.jpg</summary>
    public required string Path { get; init; }

    /// <summary>Größe in Byte laut Archiv-Kopf.</summary>
    public long Size { get; init; }

    public string Name => Path.Contains('/') ? Path[(Path.LastIndexOf('/') + 1)..] : Path;

    /// <summary>Endung in Kleinbuchstaben ohne Punkt; leer, wenn die Datei keine hat.</summary>
    public string Extension
    {
        get
        {
            var dot = Name.LastIndexOf('.');
            return dot > 0 ? Name[(dot + 1)..].ToLowerInvariant() : "";
        }
    }

    public BackupFileKind Kind => Extension switch
    {
        "jpg" or "jpeg" or "png" or "gif" or "bmp" or "svg" or "webp" or "ico" => BackupFileKind.Bild,
        "txt" or "css" or "html" or "htm" or "js" or "json" or "xml" or "md" or "csv"
            or "yaml" or "yml" or "log" => BackupFileKind.Text,
        "mp3" or "wav" or "ogg" or "m4a" or "flac" => BackupFileKind.Audio,
        "mp4" or "webm" or "mkv" or "avi" or "mov" => BackupFileKind.Video,
        "zip" or "gz" or "tar" or "7z" or "rar" => BackupFileKind.Archiv,
        _ => BackupFileKind.Sonstiges
    };

    public string KindText => Kind.ToString();

    /// <summary>Voller Anzeigepfad wie im Admin, z. B. 0_userdata.0/bilder/aufnahme.jpg</summary>
    public string DisplayPath => Namespace + "/" + Path;

    public string SizeText => FormatSize(Size);

    /// <summary>Größe in der Einheit, die sie lesbar macht — Byte, KB oder MB.</summary>
    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes:N0} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:N1} KB";
        return $"{bytes / 1024.0 / 1024.0:N1} MB";
    }
}

/// <summary>Eine konkrete Fundstelle: welches Widget in welcher View nutzt den Datenpunkt wofür.</summary>
public sealed class VisUsage
{
    public required VisVersion Version { get; init; }

    /// <summary>VIS-Projekt, in dem die View liegt (siehe <see cref="VisFile.Project"/>).</summary>
    public string Project { get; init; } = "";

    public required string View { get; init; }

    /// <summary>
    /// Vollständige Adresse der View: „Projekt/View". Erst damit ist eine Fundstelle im
    /// Admin eindeutig wiederzufinden, wenn es mehrere Projekte gibt.
    /// </summary>
    public string ViewPath => Project.Length > 0 ? $"{Project}/{View}" : View;

    /// <summary>Widget-Schlüssel aus der View, z. B. w00109.</summary>
    public required string WidgetId { get; init; }

    /// <summary>Widget-Typ aus tpl, z. B. i-vis-universal.</summary>
    public required string Template { get; init; }

    /// <summary>Widget-Set aus widgetSet, z. B. vis-inventwo.</summary>
    public string WidgetSet { get; init; } = "";

    /// <summary>Selbst vergebener Widget-Name aus data.name — in der Praxis fast nie gesetzt.</summary>
    public string WidgetName { get; init; } = "";

    /// <summary>
    /// Das Feld, in dem der Datenpunkt steht — sagt, wozu er dient: oid = angezeigter Wert,
    /// visibility-oid = Sichtbarkeitssteuerung, countdown_oid = Countdown usw.
    /// Bei Bindings steht hier das Feld plus der Hinweis „(Binding)".
    /// </summary>
    public required string Field { get; init; }

    /// <summary>
    /// Zustandsattribut, falls das Binding nicht den Wert selbst anspricht, sondern z. B.
    /// den Zeitstempel: {…Meldung.ts} liefert hier "ts". Leer bedeutet Zugriff auf den
    /// Wert (val), also den Normalfall.
    /// </summary>
    public string Attribute { get; set; } = "";

    /// <summary>Anzeigetext für das Attribut — leer wird zu „val" (dem Standardzugriff).</summary>
    public string AttributeText => Attribute.Length == 0 ? "val" : Attribute;

    public string VersionText => Version == VisVersion.Vis1 ? "VIS 1" : "VIS 2";

    /// <summary>Anzeigename des Widgets: eigener Name, sonst ID.</summary>
    public string WidgetLabel => string.IsNullOrWhiteSpace(WidgetName) ? WidgetId : $"{WidgetId} — {WidgetName}";

    /// <summary>Kompakte Fundstellenangabe für einzeilige Darstellung.</summary>
    public string Short => Attribute.Length == 0
        ? $"{VersionText} › {View} › {WidgetLabel} ({Field})"
        : $"{VersionText} › {View} › {WidgetLabel} ({Field}, .{Attribute})";
}

/// <summary>Ein in VIS verwendeter Datenpunkt, zusammengefasst über beide VIS-Versionen.</summary>
public sealed class VisDatapoint
{
    public string Id { get; init; } = "";

    /// <summary>Views je VIS-Version, in denen der Datenpunkt vorkommt.</summary>
    public SortedSet<string> Vis1Views { get; } = new(StringComparer.OrdinalIgnoreCase);
    public SortedSet<string> Vis2Views { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Alle Fundstellen mit Widget und Feld.</summary>
    public List<VisUsage> Usages { get; } = new();

    /// <summary>Anzahl der Fundstellen (ein Widget kann den Datenpunkt mehrfach nutzen).</summary>
    public int UsageCount => Usages.Count;

    /// <summary>Anzahl verschiedener Widgets, die den Datenpunkt nutzen.</summary>
    public int WidgetCount => Usages
        .Select(u => $"{u.Version}|{u.View}|{u.WidgetId}")
        .Distinct(StringComparer.Ordinal).Count();

    /// <summary>false, wenn zu dieser ID kein Objekt im Backup existiert — kaputtes Widget.</summary>
    public bool ExistsInBackup { get; set; }

    /// <summary>Name des Objekts aus dem Backup, falls vorhanden.</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Bei einem Alias das Ziel aus common.alias.id — der Datenpunkt, den das Widget über
    /// den Alias tatsächlich anspricht. Leer bei allen anderen Datenpunkten.
    /// </summary>
    public string AliasTarget { get; set; } = "";

    /// <summary>true, wenn die ID im alias-Namensraum liegt.</summary>
    public bool IsAlias => Id.StartsWith("alias.", StringComparison.Ordinal);

    /// <summary>
    /// true, wenn es ein Alias ist, dessen Ziel im Backup nicht existiert — das Widget
    /// zeigt dann zwar auf einen gültigen Alias, der aber ins Leere führt.
    /// </summary>
    public bool AliasTargetMissing { get; set; }

    public string AliasTargetText => AliasTarget.Length == 0
        ? ""
        : AliasTargetMissing ? AliasTarget + "  (Ziel fehlt)" : AliasTarget;

    public bool InVis1 => Vis1Views.Count > 0;
    public bool InVis2 => Vis2Views.Count > 0;

    public string Vis1Text => InVis1 ? "Ja" : "Nein";
    public string Vis2Text => InVis2 ? "Ja" : "Nein";
    public string ExistsText => ExistsInBackup ? "Ja" : "FEHLT";

    public string ViewsText
    {
        get
        {
            var parts = new List<string>();
            if (InVis1) parts.Add("VIS 1: " + string.Join(", ", Vis1Views));
            if (InVis2) parts.Add("VIS 2: " + string.Join(", ", Vis2Views));
            return string.Join("   |   ", parts);
        }
    }

    /// <summary>Die verwendeten Widget-Typen, für die Übersichtsspalte.</summary>
    public string WidgetsText => string.Join(", ", Usages
        .Select(u => u.Template)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(t => t, StringComparer.OrdinalIgnoreCase));
}

/// <summary>
/// Kennzeichen der ioBroker-Installation, aus der ein Backup stammt.
///
/// Wichtigstes Merkmal ist die Installations-UUID aus <c>system.meta.uuid</c>: Sie wird
/// bei der Erstinstallation vergeben und ändert sich danach nie — sie ist damit das
/// stabilste Merkmal. Hostname, Adresse und Controller-Version dienen als Rückfallebene für
/// Backups ohne dieses Objekt.
///
/// Bewusst <b>nicht</b> übernommen werden:
/// <list type="bullet">
/// <item><c>system.config.native.secret</c> — damit verschlüsselt ioBroker Passwörter.
/// Als Kennung ideal, hat aber in einem Anzeigewerkzeug nichts zu suchen.</item>
/// <item>Ort und Koordinaten aus <c>system.config.common</c> — sie verraten den Wohnort
/// und stünden auf jedem geteilten Bildschirmfoto. Zur Unterscheidung taugen sie
/// ohnehin nicht: Zwei Systeme desselben Haushalts melden denselben Ort.</item>
/// <item>Die IPv6-Link-Local-Adresse — sie enthält die MAC-Adresse.</item>
/// </list>
/// </summary>
public sealed class SystemIdentity
{
    /// <summary>UUID aus system.meta.uuid; leer, wenn das Objekt im Backup fehlt.</summary>
    public string InstallationId { get; init; } = "";

    /// <summary>
    /// Hostname. Backitup ersetzt ihn im Host-Objekt durch den Platzhalter
    /// <c>$$__hostname__$$</c>; der echte Name wird dann aus dem from-Feld abgeleitet.
    /// </summary>
    public string Hostname { get; init; } = "";

    /// <summary>Erste IPv4-Adresse des Hosts.</summary>
    public string Address { get; init; } = "";

    /// <summary>Version des js-controllers.</summary>
    public string ControllerVersion { get; init; } = "";

    /// <summary>true, wenn wenigstens ein belastbares Merkmal vorliegt.</summary>
    public bool IsKnown => InstallationId.Length > 0 || Hostname.Length > 0 || Address.Length > 0;

    /// <summary>Gekürzte UUID für die Anzeige — zur Unterscheidung reichen acht Zeichen.</summary>
    public string ShortId => InstallationId.Length >= 8 ? InstallationId[..8] : InstallationId;

    /// <summary>Einzeilige Beschreibung des Systems.</summary>
    public string Describe()
    {
        var parts = new List<string>();
        if (Hostname.Length > 0) parts.Add(Hostname);
        if (Address.Length > 0) parts.Add(Address);
        if (ControllerVersion.Length > 0) parts.Add("js-controller " + ControllerVersion);
        if (InstallationId.Length > 0) parts.Add("ID " + ShortId + "…");

        return parts.Count == 0 ? "unbekannt" : string.Join("  ·  ", parts);
    }
}

/// <summary>Ergebnis des Herkunftsabgleichs zweier Backups.</summary>
public enum SystemMatch
{
    /// <summary>Installations-UUID in beiden vorhanden und gleich — sicher dasselbe System.</summary>
    Same,
    /// <summary>Keine UUID, aber Hostname und Adresse stimmen überein — sehr wahrscheinlich dasselbe System.</summary>
    Probable,
    /// <summary>Die Merkmale widersprechen sich — mit hoher Wahrscheinlichkeit zwei verschiedene Systeme.</summary>
    Different,
    /// <summary>Mindestens ein Backup trägt keine Kennzeichen — die Herkunft ist nicht prüfbar.</summary>
    Unknown
}

/// <summary>Das Gesamtergebnis eines Ladevorgangs.</summary>
public sealed class BackupData
{
    public required string SourceFile { get; init; }
    public required BackupKind Kind { get; init; }

    /// <summary>Erstellungsdatum, aus dem Dateinamen geparst; Fallback Datei-Zeitstempel.</summary>
    public DateTime? CreatedAt { get; init; }

    public List<IobObject> Objects { get; init; } = new();
    public List<ScriptInfo> Scripts { get; init; } = new();
    public List<AdapterInstance> Instances { get; init; } = new();

    /// <summary>
    /// Datenpunkte, die Adapter-Instanzen in ihrer eigenen Konfiguration nennen — geprüft
    /// gegen den Objektbestand. Leer, wenn keine gefunden wurden; leer auch dann, wenn das
    /// Backup keine native-Abschnitte mitbringt (siehe <see cref="HasAdapterConfig"/>).
    /// </summary>
    public List<AdapterRef> AdapterRefs { get; init; } = new();

    /// <summary>
    /// true, wenn im Backup überhaupt Adapter-Konfigurationen mit Inhalt stehen. Fehlen
    /// sie, ist „kein Adapter-Verweis" kein Befund, sondern eine fehlende Quelle — die
    /// Oberfläche muss das unterscheiden können.
    /// </summary>
    public bool HasAdapterConfig { get; init; }

    /// <summary>Anzahl States aus states.jsonl. Werte werden bewusst nicht geladen.</summary>
    public int StateCount { get; init; }

    /// <summary>
    /// States aus states.jsonl, nach ID. Enthält nur Metadaten (Zeitstempel, Quelle,
    /// Qualität) — nie den Wert selbst. Leer bei Skript-Backups und bei älteren
    /// Backitup-Versionen ohne states.jsonl.
    ///
    /// Ordinal, weil ioBroker-IDs case-sensitiv sind.
    /// </summary>
    public Dictionary<string, StateInfo> States { get; init; } = new(StringComparer.Ordinal);

    /// <summary>Die vis-views.json-Dateien aus dem Backup, getrennt nach VIS 1 und VIS 2.</summary>
    public List<VisFile> VisViews { get; init; } = new();

    /// <summary>
    /// Kenndaten der Dateien aus dem files/-Baum (Admin → Dateien). Ohne Inhalte; der
    /// Export liest sie über <see cref="SourceFile"/> nach. Leer bei losen Einzeldateien
    /// und bei Skript-Backups, die keinen files/-Baum mitbringen.
    /// </summary>
    public List<BackupFileInfo> Files { get; init; } = new();

    /// <summary>
    /// Kennzeichen der Installation, aus der das Backup stammt. Bei Skript-Backups leer —
    /// dort fehlen die Systemobjekte.
    /// </summary>
    public SystemIdentity System { get; init; } = new();

    /// <summary>Objekte/Zeilen, die nicht geparst werden konnten.</summary>
    public int SkippedCount { get; init; }

    /// <summary>
    /// Ergebnis der Backup-Prüfung (JSON-Validierung nach js-controller-Vorbild). Bei
    /// klassischen Einzeldateien nicht durchgeführt (WasChecked=false).
    /// </summary>
    public BackupValidation Validation { get; init; } = new();

    public int ScriptsEnabled => Scripts.Count(s => s.Enabled);
    public int ScriptsDisabled => Scripts.Count(s => !s.Enabled);
    public int AliasCount => Objects.Count(o => o.Id.StartsWith("alias.0.", StringComparison.Ordinal));
    public int UserDataCount => Objects.Count(o => o.Id.StartsWith("0_userdata.0.", StringComparison.Ordinal));
    public int EnumCount => Objects.Count(o => o.Id.StartsWith("enum.", StringComparison.Ordinal));
}

/// <summary>Gesamturteil der Backup-Prüfung.</summary>
public enum BackupHealth
{
    /// <summary>Alle geprüften JSON-Dateien sind gültig.</summary>
    Valid,
    /// <summary>Pflichtdateien in Ordnung, aber optionale JSON-Dateien beschädigt (nur Warnung).</summary>
    Warnings,
    /// <summary>Eine Pflichtdatei (objects.jsonl/states.jsonl) enthält ungültiges JSON — Backup beschädigt.</summary>
    Invalid,
    /// <summary>Nicht geprüft (z. B. klassische Einzeldatei ohne JSONL).</summary>
    Unknown
}

/// <summary>
/// Prüfergebnis einer JSONL-Pflichtdatei (objects.jsonl / states.jsonl). Der js-controller
/// parst jede Zeile strikt; eine einzige ungültige Zeile macht das Backup beschädigt.
/// </summary>
public sealed class JsonlCheck
{
    public required string File { get; init; }
    public bool Present { get; set; }
    public int Lines { get; set; }
    public int InvalidLines { get; set; }

    /// <summary>Zeilennummer des ersten Fehlers (1-basiert); 0 = kein Fehler.</summary>
    public int FirstErrorLine { get; set; }
    public string FirstError { get; set; } = "";

    public bool Ok => Present && InvalidLines == 0;

    public string StatusText => !Present ? "fehlt" : InvalidLines == 0 ? "gültig" : "BESCHÄDIGT";

    public string Detail => !Present
        ? "Datei nicht im Backup enthalten"
        : InvalidLines == 0
            ? $"{Lines:N0} Zeilen, alle gültig"
            : $"{InvalidLines:N0} von {Lines:N0} Zeilen ungültig — zuerst Zeile {FirstErrorLine}: {FirstError}";
}

/// <summary>Prüfergebnis einer einzelnen JSON-Datei aus dem files/-Baum (optional).</summary>
public sealed class JsonFileCheck
{
    public required string Path { get; init; }
    public bool Valid { get; init; }
    public string Error { get; init; } = "";

    /// <summary>Pfad ohne führendes backup/files/ — für die Anzeige.</summary>
    public string ShortPath
    {
        get
        {
            var idx = Path.IndexOf("files/", StringComparison.OrdinalIgnoreCase);
            return idx >= 0 ? Path[(idx + "files/".Length)..] : Path;
        }
    }

    public string StatusText => Valid ? "gültig" : "BESCHÄDIGT";
}

/// <summary>
/// Ergebnis der Backup-Prüfung — bildet die JSON-Validierung des js-controllers nach
/// (packages/cli/src/lib/setup/setupBackup.ts, _validateTempDirectory):
/// objects.jsonl/states.jsonl zeilenweise strikt = Pflicht (harter Fehler); files/**/*.json
/// strikt = optional (nur Warnung). Zusätzlich wird die Vollständigkeit der Pflichtdateien
/// festgehalten.
/// </summary>
public sealed class BackupValidation
{
    public JsonlCheck Objects { get; } = new() { File = "objects.jsonl" };
    public JsonlCheck States { get; } = new() { File = "states.jsonl" };

    /// <summary>Geprüfte JSON-Dateien aus dem files/-Baum (inkl. vis-views.json).</summary>
    public List<JsonFileCheck> OptionalFiles { get; } = new();

    /// <summary>true, wenn die JSONL-Prüfung überhaupt lief (Voll-Backup / objects.jsonl).</summary>
    public bool WasChecked { get; set; }

    /// <summary>Anzahl übersprungener Objekte beim Laden (kaputte, aber tolerierte Zeilen).</summary>
    public int SkippedObjects { get; set; }

    /// <summary>
    /// true, wenn das Archiv vorzeitig endete — abgebrochener Download, voller
    /// Datenträger, beschädigte Datei.
    ///
    /// <b>Warum das eine eigene Angabe braucht:</b> Ein abgeschnittenes Archiv liefert
    /// die früh liegenden Pflichtdateien vollständig und wirkt deshalb tadellos. Gemessen
    /// an einem auf die Hälfte gekürzten echten Backup: objects.jsonl und states.jsonl
    /// waren komplett lesbar, der gesamte files/-Baum dahinter fehlte — das Urteil lautete
    /// „Backup gültig". Wer sich darauf verlässt, spielt ein halbes Backup ein.
    /// </summary>
    public bool ArchiveTruncated { get; set; }

    /// <summary>Anzahl gelesener Archiv-Einträge bis zum Abbruch — grenzt die Stelle ein.</summary>
    public int EntriesRead { get; set; }

    /// <summary>
    /// Pfade von Pflichtdateien, die <b>nicht</b> an ihrem Platz lagen und deshalb nicht
    /// verwendet wurden — etwa eine zweite objects.jsonl tief im Archiv.
    ///
    /// <b>Warum das gemeldet wird:</b> Die Erkennung ging früher nur nach dem Dateinamen.
    /// Ein Archiv mit einem eingepackten zweiten Backup — ein Adapter, der seine eigenen
    /// Daten mitsichert, ein versehentlich mitkopiertes Backup — überschrieb damit
    /// stillschweigend die echte Objektliste, und das Werkzeug zeigte anschließend die
    /// falsche Anlage an. Das macht keine Symptome; man sieht nur Zahlen, die nicht stimmen.
    /// </summary>
    public List<string> IgnoredDuplicates { get; } = new();

    public int OptionalCount => OptionalFiles.Count;
    public int OptionalInvalid => OptionalFiles.Count(f => !f.Valid);

    public bool HasRequiredError => Objects.InvalidLines > 0 || States.InvalidLines > 0;

    public BackupHealth Health =>
        !WasChecked ? BackupHealth.Unknown
        // Ein unvollständiges Archiv wiegt schwerer als jede einzelne kaputte Datei:
        // Was fehlt, kann man nicht prüfen.
        : ArchiveTruncated ? BackupHealth.Invalid
        : HasRequiredError ? BackupHealth.Invalid
        : OptionalInvalid > 0 ? BackupHealth.Warnings
        : BackupHealth.Valid;

    /// <summary>Kurzer Klartext des Urteils — angelehnt an „The backup is valid!".</summary>
    public string HealthText => Health switch
    {
        BackupHealth.Valid => "Backup gültig — alle JSON-Dateien sind wohlgeformt.",
        BackupHealth.Warnings => $"Backup nutzbar, aber {OptionalInvalid} optionale JSON-Datei(en) beschädigt.",
        BackupHealth.Invalid when ArchiveTruncated =>
            $"Backup unvollständig — das Archiv bricht nach {EntriesRead} Einträgen ab. " +
            "Was dahinter liegt, fehlt und konnte nicht geprüft werden.",
        BackupHealth.Invalid => "Backup beschädigt — eine Pflichtdatei enthält ungültiges JSON.",
        _ => "Backup-Prüfung nicht durchgeführt (keine JSONL-Struktur)."
    };
}

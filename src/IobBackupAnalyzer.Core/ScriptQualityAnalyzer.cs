using System.Xml;

namespace IobBackupAnalyzer.Core;

/// <summary>Die Art eines Hinweises — bestimmt Text und Reihenfolge in der Anzeige.</summary>
/// <remarks>
/// Die Reihenfolge ist die Anzeigereihenfolge: Was ein Skript wirkungslos macht, steht vorn,
/// Schönheitsfehler hinten.
/// </remarks>
public enum ScriptHintKind
{
    /// <summary>Der Haken „Debuggen" ist gesetzt — das Skript schreibt nichts.</summary>
    DebugMode,
    /// <summary>Ein Trigger steht im Rumpf eines anderen Triggers.</summary>
    TriggerInTrigger,
    /// <summary>„Steuern" auf einem eigenen Datenpunkt, den niemand quittiert.</summary>
    ControlOnOwnState,
    /// <summary>„Aktualisieren" auf einem Adapter-Datenpunkt — der Adapter führt nichts aus.</summary>
    UpdateOnAdapterState,
    /// <summary>Ein Baustein, den der javascript-Adapter selbst als abgelöst kennzeichnet.</summary>
    DeprecatedBlock,
    /// <summary>„Ausführliche Protokollausgaben" sind eingeschaltet.</summary>
    VerboseLogging,
    /// <summary>Ein Trigger ohne Inhalt — er löst aus, tut aber nichts.</summary>
    TriggerWithoutBody
}

/// <summary>Ein einzelner Befund an einem Blockly-Baustein.</summary>
/// <param name="Kind">Welche Regel angeschlagen hat.</param>
/// <param name="BlockType">Der Blocktyp aus dem XML, z. B. <c>on</c> oder <c>request</c>.</param>
/// <param name="BlockId">
/// Die Block-ID aus dem XML. Sie steht auch im Blockly-Editor an genau diesem Baustein und
/// ist damit der Weg, den Befund im Skript wiederzufinden. Leer, wenn das XML keine trägt.
/// </param>
/// <param name="Detail">
/// Zusatzangabe, die erst den Befund greifbar macht — bei den ack-Befunden die Datenpunkt-ID.
/// Leer, wo der Blocktyp schon alles sagt.
/// </param>
/// <param name="Disabled">
/// true, wenn der Baustein selbst oder einer seiner übergeordneten Bausteine im Blockly-
/// Editor abgeschaltet ist (<c>disabled="true"</c>).
///
/// Der Befund bleibt trotzdem stehen, wird aber als Möglichkeit formuliert statt als
/// Tatsache: Ein abgeschalteter Baustein richtet heute keinen Schaden an — er tut es in dem
/// Augenblick, in dem jemand ihn wieder einschaltet. Ihn zu verschweigen hieße, genau diese
/// Falle zuzudecken; ihn wie einen laufenden zu melden, wäre schlicht unwahr.
/// </param>
public sealed record ScriptHint(ScriptHintKind Kind, string BlockType, string BlockId,
                                string Detail = "", bool Disabled = false)
{
    /// <summary>Vorsatz für abgeschaltete Bausteine — sonst leer.</summary>
    private string Vorsatz => Disabled ? "Abgeschalteter Baustein — " : "";

    /// <summary>Nachsatz, der den Unterschied benennt. Nur bei abgeschalteten Bausteinen.</summary>
    private string Nachsatz => Disabled
        ? " Der Baustein ist im Editor abgeschaltet und läuft derzeit nicht; der Befund "
        + "greift, sobald er wieder eingeschaltet wird."
        : "";

    /// <summary>Kurzform für die Listenspalte.</summary>
    public string ShortText => Vorsatz + KurzRoh;

    private string KurzRoh => Kind switch
    {
        ScriptHintKind.DebugMode => "Debug-Modus aktiv",
        ScriptHintKind.TriggerInTrigger => "Trigger im Trigger",
        ScriptHintKind.ControlOnOwnState => "steuern statt aktualisieren",
        ScriptHintKind.UpdateOnAdapterState => "aktualisieren statt steuern",
        ScriptHintKind.DeprecatedBlock => $"abgelöst: {BlockType}",
        ScriptHintKind.VerboseLogging => "ausführliches Protokoll",
        _ => "Trigger ohne Inhalt"
    };

    /// <summary>Der ausformulierte Befund samt Begründung — für die Anzeige unter der Liste.</summary>
    public string LongText => LangRoh + Nachsatz;

    private string LangRoh => Kind switch
    {
        ScriptHintKind.DebugMode =>
            "Debug-Modus aktiv: Am Skript ist der Haken „Debuggen\" gesetzt. Das ist kein "
          + "Protokollschalter — der javascript-Adapter führt das Skript zwar aus, "
          + "unterdrückt aber jede schreibende Operation: setState, exec und writeFile "
          + "passieren nicht, sondern werden nur als Warnung protokolliert "
          + "(„was not executed, while debug mode is active\"). Das Skript läuft also und "
          + "bewirkt nichts, ohne dass ein Fehler auffällt. Abhilfe: Im Blockly-Editor unter "
          + "dem Zahnrad den Haken „Debuggen\" entfernen.",

        ScriptHintKind.VerboseLogging =>
            "Ausführliche Protokollausgaben: Am Skript ist der Haken „Ausführliche "
          + "Protokollausgaben\" gesetzt. Der javascript-Adapter schreibt dann jede interne "
          + "Operation des Skripts ins Protokoll — bei einem Skript, das oft auslöst, füllt "
          + "das die Logdatei und macht die übrigen Meldungen unlesbar. Zum Suchen eines "
          + "Fehlers gedacht, nicht für den Dauerbetrieb. Abhilfe: Im Blockly-Editor unter "
          + "dem Zahnrad den Haken entfernen.",

        ScriptHintKind.ControlOnOwnState =>
            $"„Steuern\" auf eigenem Datenpunkt ({Detail}): Der Baustein „Zustand steuern\" "
          + "schreibt den Wert als Befehl, also unquittiert (ack=false). Einen Befehl "
          + "quittiert normalerweise der Adapter, sobald er ihn ausgeführt hat. Bei einem "
          + "selbst angelegten Datenpunkt (0_userdata, javascript) gibt es keinen Adapter, "
          + "der das täte — der Wert bleibt dauerhaft unquittiert stehen. Genau das ist hier "
          + "der Fall: Der Datenpunkt liegt im Backup mit ack=false. Richtig wäre der "
          + "Baustein „Zustand aktualisieren\"; er schreibt denselben Wert quittiert.\n\n"
          + "Datenpunkte, die ein anderes Skript als Befehl entgegennimmt, sind hiervon "
          + "ausgenommen und werden nicht gemeldet — dort ist „steuern\" richtig. Nicht "
          + "ausgenommen ist ein Sammelskript, das nur quittiert und sonst nichts tut: Solche "
          + "Skripte entstehen, weil unquittierte Werte in der Objektübersicht rot erscheinen "
          + "und nach dem Quittieren weiß. Das behebt die Farbe, nicht die Ursache — wer hier "
          + "auf „aktualisieren\" umstellt, braucht das Sammelskript für diesen Datenpunkt "
          + "nicht mehr.",

        ScriptHintKind.UpdateOnAdapterState =>
            $"„Aktualisieren\" auf Adapter-Datenpunkt ({Detail}): Der Baustein „Zustand "
          + "aktualisieren\" schreibt den Wert quittiert (ack=true) — als wäre er vom Gerät "
          + "gemeldet worden. Ein Adapter reagiert aber nur auf unquittierte Änderungen. "
          + "Er führt hier also nichts aus; das Gerät bleibt, wie es war, und der "
          + "eingetragene Wert wird beim nächsten echten Wert des Adapters überschrieben. "
          + "Richtig wäre der Baustein „Zustand steuern\".",

        ScriptHintKind.TriggerInTrigger =>
            $"Trigger im Trigger ({BlockType}): Dieser Auslöser steht im Rumpf eines anderen "
          + "Auslösers. Er wird deshalb bei jeder Auslösung des äußeren Triggers erneut "
          + "angelegt und nie wieder entfernt. Nach einigen Stunden laufen dieselben Aktionen "
          + "vielfach parallel. Der Blockly-Editor zeigt an diesem Baustein selbst ein "
          + "Warndreieck. Abhilfe: den inneren Auslöser aus dem Rumpf herausziehen und "
          + "daneben stellen.",

        ScriptHintKind.DeprecatedBlock =>
            $"Abgelöster Baustein ({BlockType}): Der javascript-Adapter führt diesen Baustein "
          + "selbst mit dem Zusatz „(deprecated)\". Er funktioniert derzeit noch, wird aber "
          + "nicht mehr gepflegt und kann in einer späteren Adapter-Fassung entfallen. "
          + "Abhilfe: durch den Nachfolger ersetzen — beim Abruf einer URL ist das „HTTP-Get\".",

        _ =>
            $"Trigger ohne Inhalt ({BlockType}): Dieser Auslöser hat keinen Rumpf. Er reagiert "
          + "also auf Änderungen, führt aber nichts aus. Meist ein Überbleibsel vom Umbauen. "
          + "Abhilfe: entweder füllen oder löschen."
    };
}

/// <summary>
/// Prüft Blockly-Skripte auf Muster, die im laufenden Betrieb Ärger machen.
///
/// <b>Warum nur Blockly?</b> Im XML hängt jeder Befund an einem benannten Baustein mit
/// eigener ID — die Aussage ist damit eindeutig und im Editor wiederfindbar. Dieselben
/// Muster in JavaScript zu suchen hieße, Text mit regulären Ausdrücken zu deuten; ein
/// <c>on(</c> in einem Kommentar oder in einer Zeichenkette wäre nicht zu unterscheiden.
/// Lieber drei belastbare Aussagen als fünf, von denen zwei nicht stimmen.
///
/// <b>Warum keine Note?</b> Es gibt keine Punktzahl und keine Bewertung des Skripts. Was
/// hier steht, sind einzelne Befunde mit Begründung — was daraus folgt, entscheidet die
/// Person, die das Skript geschrieben hat.
/// </summary>
public static class ScriptQualityAnalyzer
{
    /// <summary>
    /// Auslöser-Bausteine, die ihre Registrierung dauerhaft anlegen. Genau diese dürfen
    /// nicht im Rumpf eines anderen Auslösers stehen und sind ohne Rumpf wirkungslos.
    ///
    /// Ermittelt aus den Blockdefinitionen des javascript-Adapters
    /// (<c>src-editor/src/Components/blockly-plugins/blocks/blocks_trigger.ts</c>): alle
    /// Trigger-Bausteine mit einem Rumpf-Eingang <c>STATEMENT</c>.
    ///
    /// Bewusst <b>nicht</b> dabei ist <c>schedule_create</c>: Dieser Baustein ist gerade
    /// dafür gedacht, zur Laufzeit einen benannten Zeitplan anzulegen, den
    /// <c>schedule_clear</c> wieder entfernt. Er gehört in den Rumpf eines Triggers und
    /// wäre dort als Befund schlicht falsch.
    /// </summary>
    private static readonly HashSet<string> TriggerBlocks = new(StringComparer.Ordinal)
    {
        "on", "on_ext", "schedule", "schedule_by_id", "astro", "onMessage", "onFile", "onLog"
    };

    /// <summary>
    /// Bausteine, die der javascript-Adapter selbst als abgelöst führt — erkennbar am
    /// Zusatz „(deprecated)" in seiner Beschriftung
    /// (<c>src-editor/src/Components/blockly-plugins/blocks/words.json</c>).
    ///
    /// Stand August 2026 ist das genau einer. Die Liste ist trotzdem eine Liste: Kommt ein
    /// weiterer dazu, gehört er hierher und sonst nirgendwo hin.
    /// </summary>
    private static readonly HashSet<string> DeprecatedBlocks = new(StringComparer.Ordinal)
    {
        "request"
    };

    /// <summary>
    /// Sucht die Befunde im dekodierten Blockly-XML. Leere Liste, wenn kein XML vorliegt
    /// (JavaScript, TypeScript, defektes Blockly) oder es sich nicht lesen lässt: Ein Skript,
    /// dessen XML nicht parst, ist bereits über <see cref="ScriptInfo.BlocklyBroken"/>
    /// gemeldet und soll hier nicht ein zweites Mal auffallen.
    /// </summary>
    /// <param name="debug">common.debug — der Haken „Debuggen" am Skript.</param>
    /// <param name="verbose">common.verbose — „Ausführliche Protokollausgaben".</param>
    public static IReadOnlyList<ScriptHint> Analyze(string? blocklyXml,
                                                    bool debug = false, bool verbose = false)
    {
        // Die beiden Schalter hängen am Skript, nicht an einem Baustein. Sie gelten deshalb
        // für jede Sprache — auch für JavaScript und TypeScript, wo es kein XML zu lesen gibt.
        var hints = new List<ScriptHint>();
        if (debug) hints.Add(new ScriptHint(ScriptHintKind.DebugMode, "", ""));
        if (verbose) hints.Add(new ScriptHint(ScriptHintKind.VerboseLogging, "", ""));

        if (string.IsNullOrEmpty(blocklyXml)) return hints;

        var doc = TryLoad(blocklyXml);
        if (doc?.DocumentElement is null) return hints;

        Walk(doc.DocumentElement, insideTrigger: false, hints);
        return hints;
    }

    /// <summary>Wem ein Datenpunkt gehört — entscheidet, welcher Schreib-Baustein richtig ist.</summary>
    public enum StateOwner
    {
        /// <summary>Nicht zuzuordnen: unbekannter Namensraum, Alias ohne auflösbares Ziel,
        /// Datenpunkt nicht im Backup. Für solche Ziele wird nichts behauptet.</summary>
        Unknown,
        /// <summary>Selbst angelegt (0_userdata, javascript) — niemand quittiert hier.</summary>
        Own,
        /// <summary>Gehört einer Adapter-Instanz, die auf unquittierte Befehle reagiert.</summary>
        Adapter
    }

    /// <summary>
    /// Prüft die Schreib-Bausteine „Zustand steuern" (<c>control</c>, schreibt ack=false) und
    /// „Zustand aktualisieren" (<c>update</c>, schreibt ack=true) gegen den Besitzer des
    /// Ziel-Datenpunkts.
    ///
    /// <b>Die Regel</b> (javascript-Adapter, <c>blocks_system.ts</c>: <c>control</c> erzeugt
    /// <c>setState(id, wert)</c>, <c>update</c> erzeugt <c>setState(id, wert, true)</c>;
    /// js-controller, <c>types-dev/index.d.ts</c> zum ack-Feld: „Direction flag: false for
    /// desired value and true for actual value"):
    /// <list type="bullet">
    /// <item>Adapter-Datenpunkt → <b>steuern</b>. Der Adapter reagiert nur auf unquittierte
    /// Änderungen und quittiert selbst, sobald er den Befehl ausgeführt hat.</item>
    /// <item>Eigener Datenpunkt → <b>aktualisieren</b>. Es gibt keinen Adapter, der quittieren
    /// könnte; unquittiert geschrieben bliebe der Wert für immer ein offener Befehl.</item>
    /// </list>
    ///
    /// <b>Warum zwei Einschränkungen:</b> „Steuern" auf einem eigenen Datenpunkt ist nicht
    /// zwangsläufig falsch — als Befehlskanal zwischen zwei Skripten ist es sogar richtig.
    /// Gemeldet wird deshalb nur, was im Backup unquittiert liegt
    /// (<paramref name="isUnacknowledged"/>) <b>und</b> von keinem Skript als Befehl
    /// entgegengenommen oder quittiert wird (<paramref name="isCommandChannel"/>, siehe
    /// <see cref="AcknowledgedStates"/>). In den Testdaten dieses Projekts sinkt die Zahl der
    /// Befunde dadurch von 63 auf 37 — und keiner davon hängt an einem Alias.
    ///
    /// <c>control_ex</c> („Zustand steuern" mit frei wählbarem ack) bleibt bewusst außen vor:
    /// Dort entscheidet die schreibende Person selbst, und beide Werte sind vertretbar.
    /// </summary>
    /// <param name="owner">Ordnet eine Datenpunkt-ID ihrem Besitzer zu.</param>
    /// <param name="isUnacknowledged">
    /// true, wenn der Datenpunkt im Backup unquittiert liegt (states.jsonl, ack=false).
    /// </param>
    /// <param name="isCommandChannel">
    /// true, wenn irgendein Skript diesen Datenpunkt als Befehl entgegennimmt oder quittiert
    /// (siehe <see cref="AcknowledgedStates"/>). Dann ist „steuern" richtig und es entsteht
    /// kein Hinweis.
    /// </param>
    public static IReadOnlyList<ScriptHint> AckHints(string? blocklyXml,
                                                     Func<string, StateOwner> owner,
                                                     Func<string, bool> isUnacknowledged,
                                                     Func<string, bool>? isCommandChannel = null)
    {
        if (string.IsNullOrEmpty(blocklyXml)) return Array.Empty<ScriptHint>();

        var doc = TryLoad(blocklyXml);
        if (doc?.DocumentElement is null) return Array.Empty<ScriptHint>();

        var hints = new List<ScriptHint>();
        WalkAck(doc.DocumentElement, owner, isUnacknowledged,
                isCommandChannel ?? (_ => false), hints);
        return hints;
    }

    /// <summary>
    /// Sammelt die Datenpunkte, die dieses Skript als <b>Befehl</b> entgegennimmt oder
    /// <b>quittiert</b>. Wer hier steht, ist ein Befehlskanal — auf ihn gehört „steuern",
    /// und ein Hinweis darauf wäre falsch.
    ///
    /// Der Beleg ist ein Auslöser, der <b>etwas tut</b> und dabei entweder auf Befehle
    /// lauscht (<c>ACK_CONDITION=false</c>) oder den auslösenden Datenpunkt quittiert
    /// (Baustein <c>on_ack_value</c>, erzeugt
    /// <c>setStateAsync(obj.id, { val: obj.state.val, ack: true })</c>) — Blockdefinitionen
    /// des javascript-Adapters, <c>blocks_trigger.ts</c>.
    ///
    /// <b>Bewusst kein Beleg</b> ist ein <c>update</c>-Baustein auf denselben Datenpunkt.
    /// Dass irgendwo jemand denselben Wert quittiert schreibt, macht aus dem Datenpunkt
    /// keinen Befehlskanal — es kann ebenso gut Uneinheitlichkeit sein, und die zu
    /// verschweigen wäre falsch herum. Ob der Befehl offen stehen bleibt, beantwortet
    /// ohnehin schon der ack-Zustand aus states.jsonl.
    ///
    /// <b>Warum „etwas tut" dazugehört:</b> Ein Auslöser, dessen Rumpf <i>nur</i> aus dem
    /// Quittier-Baustein besteht, ist kein Befehlskanal, sondern ein Pflaster. Solche
    /// Sammelskripte („ACK") entstehen, weil unquittierte Datenpunkte in der Objektübersicht
    /// rot dargestellt werden; das Quittieren macht sie weiß. Behoben ist damit die Farbe,
    /// nicht die Ursache — geschrieben werden müsste an der Quelle mit „aktualisieren".
    /// Würde so ein Skript als Befehlskanal zählen, verschwiege das Werkzeug genau den
    /// Befund, der das Sammelskript überflüssig machen würde.
    /// </summary>
    public static IReadOnlyCollection<string> AcknowledgedStates(string? blocklyXml)
    {
        if (string.IsNullOrEmpty(blocklyXml)) return Array.Empty<string>();

        var doc = TryLoad(blocklyXml);
        if (doc?.DocumentElement is null) return Array.Empty<string>();

        var found = new HashSet<string>(StringComparer.Ordinal);
        WalkAcknowledged(doc.DocumentElement, found);
        return found;
    }

    private static void WalkAcknowledged(XmlNode node, HashSet<string> found)
    {
        if (node.NodeType == XmlNodeType.Element && node.LocalName == "block")
        {
            var type = node.Attributes?["type"]?.Value ?? "";
            var oid = FieldValue(node, "OID");

            if (oid.Length > 0)
            {
                if (TriggerBlocks.Contains(type) && DoesRealWork(node)
                    && (FieldValue(node, "ACK_CONDITION") == "false" || AcknowledgesInBody(node)))
                    found.Add(oid);
            }
        }

        foreach (XmlNode child in node.ChildNodes)
            WalkAcknowledged(child, found);
    }

    /// <summary>
    /// Tut der Auslöser etwas anderes als quittieren? Gezählt wird jeder Baustein in seinem
    /// Rumpf, der nicht <c>on_ack_value</c> ist. Ein Rumpf, der nur quittiert, ist reine
    /// Kosmetik gegen die rote Darstellung unquittierter Werte — siehe
    /// <see cref="AcknowledgedStates"/>.
    /// </summary>
    private static bool DoesRealWork(XmlNode trigger)
    {
        foreach (XmlNode child in trigger.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element || child.LocalName != "statement") continue;
            if (HasNonAckBlock(child)) return true;
        }

        return false;
    }

    private static bool HasNonAckBlock(XmlNode node)
    {
        if (node.NodeType == XmlNodeType.Element && node.LocalName == "block")
        {
            var type = node.Attributes?["type"]?.Value ?? "";

            // Ein weiterer Auslöser bringt seinen eigenen Rumpf mit — der zählt dort, nicht hier.
            if (TriggerBlocks.Contains(type)) return false;
            if (type != "on_ack_value") return true;
        }

        foreach (XmlNode child in node.ChildNodes)
            if (HasNonAckBlock(child)) return true;

        return false;
    }

    /// <summary>
    /// Steht im Rumpf dieses Auslösers der Baustein „quittieren" (<c>on_ack_value</c>)?
    /// Gesucht wird nur im eigenen Rumpf: Ein Auslöser, der über <c>&lt;next&gt;</c> daneben
    /// hängt, bringt seinen eigenen Rumpf mit und quittiert seinen eigenen Datenpunkt.
    /// </summary>
    private static bool AcknowledgesInBody(XmlNode trigger)
    {
        foreach (XmlNode child in trigger.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element || child.LocalName != "statement") continue;
            if (ContainsAckBlock(child)) return true;
        }

        return false;
    }

    private static bool ContainsAckBlock(XmlNode node)
    {
        if (node.NodeType == XmlNodeType.Element && node.LocalName == "block")
        {
            var type = node.Attributes?["type"]?.Value ?? "";
            if (type == "on_ack_value") return true;

            // Ein weiterer Auslöser bringt seinen eigenen Rumpf mit — der zählt nicht hierher.
            if (TriggerBlocks.Contains(type)) return false;
        }

        foreach (XmlNode child in node.ChildNodes)
            if (ContainsAckBlock(child)) return true;

        return false;
    }

    private static void WalkAck(XmlNode node, Func<string, StateOwner> owner,
                                Func<string, bool> isUnacknowledged,
                                Func<string, bool> isCommandChannel, List<ScriptHint> hints)
    {
        if (node.NodeType == XmlNodeType.Element && node.LocalName == "block")
        {
            var type = node.Attributes?["type"]?.Value ?? "";

            if (type is "control" or "update")
            {
                var oid = FieldValue(node, "OID");
                if (!string.IsNullOrWhiteSpace(oid))
                {
                    var id = node.Attributes?["id"]?.Value ?? "";
                    var who = owner(oid);

                    if (type == "update" && who == StateOwner.Adapter)
                        hints.Add(new ScriptHint(ScriptHintKind.UpdateOnAdapterState, type, id, oid));

                    // „Steuern" auf einem eigenen Datenpunkt ist nur dann ein Befund, wenn
                    // ihn niemand quittiert UND ihn auch niemand als Befehl erwartet.
                    if (type == "control" && who == StateOwner.Own
                        && isUnacknowledged(oid) && !isCommandChannel(oid))
                        hints.Add(new ScriptHint(ScriptHintKind.ControlOnOwnState, type, id, oid));
                }
            }
        }

        foreach (XmlNode child in node.ChildNodes)
            WalkAck(child, owner, isUnacknowledged, isCommandChannel, hints);
    }

    /// <summary>
    /// Der Wert eines Feldes des Blocks selbst. Gesucht wird nur unter den <b>direkten</b>
    /// Kindern: Ein verschachtelter Block bringt eigene Felder mit, und ein „OID" aus einem
    /// eingesetzten Wert-Baustein gehört nicht zu diesem Block.
    /// </summary>
    private static string FieldValue(XmlNode block, string name)
    {
        foreach (XmlNode child in block.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element || child.LocalName != "field") continue;
            if (child.Attributes?["name"]?.Value == name) return child.InnerText.Trim();
        }

        return "";
    }

    /// <summary>Lädt das XML; null, wenn es sich nicht lesen lässt.</summary>
    private static XmlDocument? TryLoad(string xml)
    {
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            return doc;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    /// <summary>
    /// Läuft den Blockbaum ab. <paramref name="insideTrigger"/> sagt, ob der aktuelle Knoten
    /// im <i>Rumpf</i> eines Auslösers hängt.
    ///
    /// <b>Der entscheidende Unterschied:</b> In Blockly hat ein Block zwei Arten von
    /// Nachfolgern. <c>&lt;next&gt;</c> ist der Block, der <i>darunter</i> angedockt ist —
    /// ein Nachbar auf derselben Ebene. <c>&lt;statement&gt;</c> ist der Inhalt <i>innerhalb</i>
    /// des Blocks. Im XML sehen beide gleich aus: eingerückte <c>&lt;block&gt;</c>-Elemente.
    ///
    /// Wer das nicht trennt, meldet jede Anlage falsch: Mehrere Auslöser nebeneinander sind
    /// der Normalfall und hängen über <c>&lt;next&gt;</c> aneinander. In den Testdaten dieses
    /// Projekts erzeugt die ungenaue Prüfung 78 Treffer, die genaue null.
    /// </summary>
    private static void Walk(XmlNode node, bool insideTrigger, List<ScriptHint> hints,
                             bool insideDisabled = false)
    {
        var isTrigger = false;

        // Abgeschaltet wird vererbt: Steht der äußere Baustein auf disabled, läuft auch
        // nichts, was in ihm steckt. Blockly setzt das Attribut nur am obersten Baustein
        // einer abgeschalteten Gruppe.
        var disabled = insideDisabled;

        if (node.NodeType == XmlNodeType.Element && node.LocalName == "block")
        {
            var type = node.Attributes?["type"]?.Value ?? "";
            var id = node.Attributes?["id"]?.Value ?? "";

            if (node.Attributes?["disabled"]?.Value == "true") disabled = true;

            isTrigger = TriggerBlocks.Contains(type);

            if (isTrigger && insideTrigger)
                hints.Add(new ScriptHint(ScriptHintKind.TriggerInTrigger, type, id, "", disabled));

            if (isTrigger && !HasBody(node))
                hints.Add(new ScriptHint(ScriptHintKind.TriggerWithoutBody, type, id, "", disabled));

            if (DeprecatedBlocks.Contains(type))
                hints.Add(new ScriptHint(ScriptHintKind.DeprecatedBlock, type, id, "", disabled));
        }

        foreach (XmlNode child in node.ChildNodes)
        {
            // <next> führt zum Nachbarblock — der steht neben diesem Block, nicht darin.
            // Der Rumpf-Zustand wird deshalb unverändert weitergereicht.
            var childInside = child.NodeType == XmlNodeType.Element && child.LocalName == "next"
                ? insideTrigger
                : insideTrigger || isTrigger;

            // Der Abschaltzustand folgt derselben Regel wie der Rumpf-Zustand: Über <next>
            // geht es zum Nachbarn, und der kann sehr wohl eingeschaltet sein, während
            // dieser Baustein aus ist.
            var childDisabled = child.NodeType == XmlNodeType.Element && child.LocalName == "next"
                ? insideDisabled
                : disabled;

            Walk(child, childInside, hints, childDisabled);
        }
    }

    /// <summary>
    /// Hat der Auslöser einen gefüllten Rumpf? Gesucht wird ein direktes
    /// <c>&lt;statement&gt;</c>-Kind, in dem mindestens ein Block steht. Ein leeres
    /// <c>&lt;statement&gt;</c> schreibt Blockly gar nicht erst — geprüft wird es trotzdem,
    /// weil das XML auch von Hand bearbeitet worden sein kann.
    /// </summary>
    private static bool HasBody(XmlNode block)
    {
        foreach (XmlNode child in block.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element || child.LocalName != "statement") continue;

            foreach (XmlNode inner in child.ChildNodes)
                if (inner.NodeType == XmlNodeType.Element && inner.LocalName == "block")
                    return true;
        }

        return false;
    }
}

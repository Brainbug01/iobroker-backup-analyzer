using System.Xml;

namespace IobBackupAnalyzer.Core;

/// <summary>Die Art eines Hinweises — bestimmt Text und Reihenfolge in der Anzeige.</summary>
public enum ScriptHintKind
{
    /// <summary>Ein Trigger steht im Rumpf eines anderen Triggers.</summary>
    TriggerInTrigger,
    /// <summary>Ein Baustein, den der javascript-Adapter selbst als abgelöst kennzeichnet.</summary>
    DeprecatedBlock,
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
public sealed record ScriptHint(ScriptHintKind Kind, string BlockType, string BlockId)
{
    /// <summary>Kurzform für die Listenspalte.</summary>
    public string ShortText => Kind switch
    {
        ScriptHintKind.TriggerInTrigger => "Trigger im Trigger",
        ScriptHintKind.DeprecatedBlock => $"abgelöst: {BlockType}",
        _ => "Trigger ohne Inhalt"
    };

    /// <summary>Der ausformulierte Befund samt Begründung — für die Anzeige unter der Liste.</summary>
    public string LongText => Kind switch
    {
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
    public static IReadOnlyList<ScriptHint> Analyze(string? blocklyXml)
    {
        if (string.IsNullOrEmpty(blocklyXml)) return Array.Empty<ScriptHint>();

        XmlDocument doc;
        try
        {
            doc = new XmlDocument();
            doc.LoadXml(blocklyXml);
        }
        catch (XmlException)
        {
            return Array.Empty<ScriptHint>();
        }

        if (doc.DocumentElement is null) return Array.Empty<ScriptHint>();

        var hints = new List<ScriptHint>();
        Walk(doc.DocumentElement, insideTrigger: false, hints);
        return hints;
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
    private static void Walk(XmlNode node, bool insideTrigger, List<ScriptHint> hints)
    {
        var isTrigger = false;

        if (node.NodeType == XmlNodeType.Element && node.LocalName == "block")
        {
            var type = node.Attributes?["type"]?.Value ?? "";
            var id = node.Attributes?["id"]?.Value ?? "";

            isTrigger = TriggerBlocks.Contains(type);

            if (isTrigger && insideTrigger)
                hints.Add(new ScriptHint(ScriptHintKind.TriggerInTrigger, type, id));

            if (isTrigger && !HasBody(node))
                hints.Add(new ScriptHint(ScriptHintKind.TriggerWithoutBody, type, id));

            if (DeprecatedBlocks.Contains(type))
                hints.Add(new ScriptHint(ScriptHintKind.DeprecatedBlock, type, id));
        }

        foreach (XmlNode child in node.ChildNodes)
        {
            // <next> führt zum Nachbarblock — der steht neben diesem Block, nicht darin.
            // Der Rumpf-Zustand wird deshalb unverändert weitergereicht.
            var childInside = child.NodeType == XmlNodeType.Element && child.LocalName == "next"
                ? insideTrigger
                : insideTrigger || isTrigger;

            Walk(child, childInside, hints);
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

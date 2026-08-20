using System.Text.Json;

namespace IobBackupAnalyzer.Core;

/// <summary>
/// Wandelt einzelne JSON-Objekte in <see cref="IobObject"/> um. Gemeinsame Basis für
/// beide Quellformate: objects.jsonl (ID im Feld _id) und script.json/backup.json
/// (ID als Schlüssel).
/// </summary>
internal static class ObjectParser
{
    /// <summary>
    /// Liest ein ioBroker-Objekt aus einem JSON-Element.
    /// </summary>
    /// <param name="el">Das Objekt selbst.</param>
    /// <param name="idFromKey">
    /// ID aus dem umgebenden Schlüssel (script.json-Pfad) oder null, wenn sie aus _id kommt.
    /// </param>
    public static IobObject? Parse(JsonElement el, string? idFromKey)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;

        var id = idFromKey;
        if (id is null)
        {
            // objects.jsonl: _id steht im Objekt, die Feldreihenfolge ist nicht garantiert.
            if (el.TryGetProperty("_id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                id = idEl.GetString();
        }
        if (string.IsNullOrEmpty(id)) return null;

        var type = el.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString() ?? "" : "";

        JsonElement common = default;
        var hasCommon = el.TryGetProperty("common", out common) && common.ValueKind == JsonValueKind.Object;

        var name = hasCommon ? ReadName(common, id) : LastSegment(id);

        // Aktiv ist nur, was ausdrücklich enabled: true trägt.
        //
        // Ein Skript oder eine Instanz ohne common.enabled läuft in ioBroker NICHT: Der
        // javascript-Adapter und der js-controller prüfen das Feld in JavaScript auf
        // Wahrheitswert, und ein fehlendes Feld ist dort undefined — also falsch. Solche
        // Objekte entstehen beim Kopieren oder Importieren eines Skripts.
        //
        // Die frühere Annahme „fehlt = aktiv" war der Grund für einen falschen Status:
        // Ein aus dem realen Backup stammendes Skript ohne das Feld stand als „Aktiv" in
        // der Liste, obwohl es nicht lief. Gegenprobe im selben Backup ist der
        // Laufzeit-Datenpunkt javascript.0.scriptEnabled.<Pfad> — er steht dort auf false.
        // Für alle übrigen Skripte desselben Backups stimmt er mit common.enabled überein.
        var enabled = hasCommon
                      && common.TryGetProperty("enabled", out var en)
                      && en.ValueKind == JsonValueKind.True;

        string? version = null;
        if (hasCommon && common.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String)
            version = v.GetString();

        // common.custom ist nach Instanz geschlüsselt: { "influxdb.0": {…}, "history.1": {…} }.
        // Über alle Schlüssel iterieren — nicht auf einen Adapter oder eine feste
        // Instanznummer festlegen, damit history/sql/.1/.2 gleichermaßen erfasst werden.
        List<CustomLoggingEntry>? customLogging = null;
        if (hasCommon && common.TryGetProperty("custom", out var custom)
            && custom.ValueKind == JsonValueKind.Object)
        {
            foreach (var inst in custom.EnumerateObject())
            {
                if (inst.Value.ValueKind != JsonValueKind.Object) continue;
                var c = inst.Value;
                customLogging ??= new List<CustomLoggingEntry>();
                customLogging.Add(new CustomLoggingEntry
                {
                    Instance = inst.Name,
                    Enabled = !(c.TryGetProperty("enabled", out var cen)
                                && cen.ValueKind == JsonValueKind.False),
                    ChangesOnly = c.TryGetProperty("changesOnly", out var co)
                                  && co.ValueKind == JsonValueKind.True,
                    DebounceMs = ReadLong(c, "debounceTime") ?? ReadLong(c, "debounce") ?? 0,
                    AliasId = c.TryGetProperty("aliasId", out var cai)
                              && cai.ValueKind == JsonValueKind.String ? cai.GetString() ?? "" : ""
                });
            }
        }
        var hasCustom = customLogging is { Count: > 0 };

        // Alias: common.alias.id ist entweder ein String (Lesen = Schreiben) oder ein
        // Objekt { read, write }. Beide Ziele werden getrennt geführt; AliasTarget bleibt
        // das Lese-Ziel (bzw. ersatzweise das Schreib-Ziel) für die bestehenden Analysen.
        //
        // WICHTIG: common.alias.read/write (Geschwister von id) sind Konvertierungs-
        // FUNKTIONEN (JS-Code), nicht Ziele. Sie werden getrennt erfasst und dürfen nicht
        // mit id.read/id.write (den getrennten Ziel-IDs) verwechselt werden.
        string? aliasTarget = null, aliasRead = null, aliasWrite = null;
        string? convRead = null, convWrite = null;
        if (hasCommon && common.TryGetProperty("alias", out var alias) && alias.ValueKind == JsonValueKind.Object)
        {
            if (alias.TryGetProperty("id", out var aid))
            {
                if (aid.ValueKind == JsonValueKind.String)
                {
                    aliasRead = aliasWrite = aid.GetString();
                }
                else if (aid.ValueKind == JsonValueKind.Object)
                {
                    // Lese-/Schreib-Alias: { "read": "…", "write": "…" }
                    if (aid.TryGetProperty("read", out var rd) && rd.ValueKind == JsonValueKind.String)
                        aliasRead = rd.GetString();
                    if (aid.TryGetProperty("write", out var wr) && wr.ValueKind == JsonValueKind.String)
                        aliasWrite = wr.GetString();
                }
            }
            aliasTarget = aliasRead ?? aliasWrite;

            convRead = NonEmpty(alias, "read");
            convWrite = NonEmpty(alias, "write");
        }

        // common.states: Wertetabelle (Gerätewert → Anzeigelabel) und common.type — Grundlage
        // für den Konverter-Generator. Nur bei Datenpunkten mit Aufzählung gesetzt.
        IReadOnlyDictionary<string, string>? states = null;
        string? commonType = null;

        // common.write: Sagt, ob ein Datenpunkt beschrieben werden kann oder nur liefert
        // (Kontaktsensoren, Messwerte). Fehlt die Angabe, gilt in ioBroker „nicht
        // schreibbar" — deshalb null statt false, damit „nicht angegeben" erkennbar bleibt.
        bool? writable = null;

        if (hasCommon)
        {
            if (common.TryGetProperty("type", out var ct) && ct.ValueKind == JsonValueKind.String)
                commonType = ct.GetString();
            if (common.TryGetProperty("states", out var st))
                states = ReadStates(st);
            if (common.TryGetProperty("write", out var wr)
                && wr.ValueKind is JsonValueKind.True or JsonValueKind.False)
                writable = wr.ValueKind == JsonValueKind.True;
        }

        // Chart: native.data.l ist die Linienliste; je Linie steht in id der referenzierte
        // Datenpunkt und in instance die Quelle (system.adapter.<name>.<nr> oder z. B. "json").
        // Erfasst wird ausschließlich id — instanzunabhängig, damit auch über history oder
        // sql gespeiste Linien zählen.
        IReadOnlyList<string>? chartRefs = null;
        if (type == "chart"
            && el.TryGetProperty("native", out var native) && native.ValueKind == JsonValueKind.Object
            && native.TryGetProperty("data", out var cdata) && cdata.ValueKind == JsonValueKind.Object
            && cdata.TryGetProperty("l", out var lines) && lines.ValueKind == JsonValueKind.Array)
        {
            List<string>? refs = null;
            foreach (var ln in lines.EnumerateArray())
            {
                if (ln.ValueKind != JsonValueKind.Object) continue;
                if (ln.TryGetProperty("id", out var lid) && lid.ValueKind == JsonValueKind.String)
                {
                    var did = lid.GetString();
                    if (!string.IsNullOrWhiteSpace(did))
                    {
                        refs ??= new List<string>();
                        refs.Add(did);
                    }
                }
            }
            chartRefs = refs;
        }

        // Adapter-Instanz: Viele Adapter tragen ihre Datenpunkte in der eigenen
        // Konfiguration ein — Shuttercontrol seine Rollläden, awtrix-light die Werte seiner
        // Apps, text2command die Ziele seiner Regeln. Ohne diese Quelle steht ein solcher
        // Datenpunkt in der Verwendungsübersicht bei null, obwohl er in Gebrauch ist.
        //
        // Gesammelt werden hier nur ID-förmige Zeichenketten samt Feldpfad; welche davon
        // wirklich Datenpunkte sind, entscheidet erst der Abgleich mit dem Objektbestand
        // am Ende des Ladevorgangs. Alles ohne Treffer wird dort verworfen und ist damit
        // nie Teil einer Anzeige oder eines Exports — Passwörter und Hostnamen aus dem
        // native-Abschnitt können so nicht durchsickern.
        IReadOnlyList<AdapterRefCandidate>? nativeRefs = null;
        if (type == "instance"
            && el.TryGetProperty("native", out var instNative)
            && instNative.ValueKind == JsonValueKind.Object)
        {
            List<AdapterRefCandidate>? found = null;
            CollectIdLike(instNative, "", "", ref found);
            nativeRefs = found;
        }

        ScriptInfo? script = null;
        if (type == "script" && hasCommon)
            script = ParseScript(id, common, name, enabled);

        return new IobObject
        {
            Id = id,
            Type = type,
            Name = name,
            Version = version,
            Enabled = enabled,
            HasCustom = hasCustom,
            Writable = writable,
            CustomLogging = customLogging,
            AliasTarget = aliasTarget,
            AliasRead = aliasRead,
            AliasWrite = aliasWrite,
            ConverterRead = convRead,
            ConverterWrite = convWrite,
            States = states,
            CommonType = commonType,
            ChartRefs = chartRefs,
            NativeRefs = nativeRefs,
            Script = script
        };
    }

    /// <summary>
    /// Obergrenze je Instanz. Eine Adapter-Konfiguration mit tausenden ID-förmigen Werten
    /// gibt es nicht; die Grenze verhindert nur, dass ein ungewöhnlich großer native-Baum
    /// (etwa eine eingebettete Gerätedatenbank) unnötig Speicher bindet.
    /// </summary>
    private const int MaxNativeRefs = 500;

    /// <summary>
    /// Feldnamen, deren Wert als Beschriftung der Fundstelle taugt. <b>Bewusst eine kurze
    /// Positivliste mit exaktem Vergleich</b>: In derselben Konfiguration stehen regelmäßig
    /// „Username", „Password" und „API_Server" — die dürfen nicht mitkommen, nur weil sie
    /// zufällig neben einer Datenpunkt-ID liegen. „Username" ist nicht „name".
    /// </summary>
    private static readonly string[] LabelFields =
        { "name", "title", "label", "caption", "description", "beschreibung" };

    /// <summary>Längere „Namen" sind Fließtext und in einer Tabellenspalte unbrauchbar.</summary>
    private const int MaxLabelLength = 40;

    /// <summary>
    /// Sammelt rekursiv alle Zeichenketten aus dem native-Baum, die wie eine Objekt-ID
    /// aussehen, samt ihrem Feldpfad (<c>customApps[0].objId</c>) und — wenn vorhanden —
    /// der Beschriftung des umgebenden Eintrags. Bei awtrix-light heißt der Eintrag neben
    /// <c>objId</c> etwa „power"; das ist als Fundstelle ungleich sprechender als ein Index.
    /// </summary>
    /// <param name="label">
    /// Beschriftung des nächstgelegenen umschließenden Eintrags. Sie wird nach unten
    /// weitergereicht, damit sie auch bei verschachtelten Listen (<c>rules[3].args[0]</c>)
    /// noch zur Verfügung steht.
    /// </param>
    private static void CollectIdLike(JsonElement el, string path, string label,
                                      ref List<AdapterRefCandidate>? found)
    {
        if (found is { Count: >= MaxNativeRefs }) return;

        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                var eigenes = LabelOf(el);
                if (eigenes.Length > 0) label = eigenes;

                foreach (var p in el.EnumerateObject())
                    CollectIdLike(p.Value, path.Length == 0 ? p.Name : path + "." + p.Name,
                                  label, ref found);
                break;

            case JsonValueKind.Array:
                var i = 0;
                foreach (var item in el.EnumerateArray())
                    CollectIdLike(item, path + "[" + i++ + "]", label, ref found);
                break;

            case JsonValueKind.String:
                var s = el.GetString();
                if (LooksLikeObjectId(s))
                {
                    found ??= new List<AdapterRefCandidate>();
                    found.Add(new AdapterRefCandidate(s!, path, label));
                }
                break;
        }
    }

    /// <summary>Die Beschriftung eines Objekts, sofern eines der erlaubten Felder sie trägt.</summary>
    private static string LabelOf(JsonElement obj)
    {
        foreach (var field in LabelFields)
        {
            if (!obj.TryGetProperty(field, out var v) || v.ValueKind != JsonValueKind.String) continue;

            var value = v.GetString();
            if (string.IsNullOrWhiteSpace(value)) continue;
            if (value.Length > MaxLabelLength) continue;
            if (value.Contains('\n') || value.Contains('\r')) continue;

            // Zweiter Riegel neben der Positivliste: Ein Wert, der wie eine Kennung oder ein
            // verschlüsseltes Feld aussieht, wird auch dann nicht übernommen, wenn er in
            // einem erlaubten Feld steht. „$/aes-192-cbc:…" ist ioBrokers Kennzeichnung für
            // verschlüsselte Werte, ein „@" deutet auf eine Adresse oder einen Benutzernamen.
            if (value.Contains("$/", StringComparison.Ordinal)) continue;
            if (value.Contains('@')) continue;

            return value.Trim();
        }

        return "";
    }

    /// <summary>
    /// Grobfilter für den Zwischenspeicher: mindestens zwei Segmente, keine Zeichen, die in
    /// einer ioBroker-ID nicht vorkommen, und keine URL. Das ist bewusst nur eine Vorauswahl
    /// — verbindlich entscheidet der Abgleich mit dem Objektbestand.
    /// </summary>
    private static bool LooksLikeObjectId(string? s)
    {
        if (s is null || s.Length is < 5 or > 200) return false;
        if (!s.Contains('.')) return false;
        if (s.Contains("://", StringComparison.Ordinal)) return false;

        foreach (var c in s)
            if (c is '/' or '\\' or '"' or '\'' or '\n' or '\r' or '\t' or '{' or '}' or '<' or '>')
                return false;

        // Das erste Segment ist der Adapter- oder Namensraum-Teil und trägt nie ein Leerzeichen.
        var firstDot = s.IndexOf('.');
        return firstDot > 0 && !s[..firstDot].Contains(' ');
    }

    /// <summary>Liest ein String-Feld; leere Strings werden zu null (kein Konverter gesetzt).</summary>
    private static string? NonEmpty(JsonElement el, string property)
    {
        if (el.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String)
        {
            var s = v.GetString();
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }
        return null;
    }

    /// <summary>
    /// Liest common.states in eine Wertetabelle (Gerätewert → Anzeigelabel). ioBroker legt
    /// sie meist als Objekt ab ({"off":"Aus"}), seltener als String ("0:Aus;1:An") oder als
    /// Array (["Aus","An"], Index = Wert). Die Einfügereihenfolge bleibt erhalten.
    /// </summary>
    private static IReadOnlyDictionary<string, string>? ReadStates(JsonElement st)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        switch (st.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in st.EnumerateObject())
                    map[p.Name] = p.Value.ValueKind == JsonValueKind.String
                        ? p.Value.GetString() ?? "" : p.Value.ToString();
                break;

            case JsonValueKind.String:
                var s = st.GetString();
                if (string.IsNullOrEmpty(s)) return null;
                foreach (var part in s.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = part.Split(':', 2);
                    if (kv.Length == 2) map[kv[0].Trim()] = kv[1].Trim();
                }
                break;

            case JsonValueKind.Array:
                var i = 0;
                foreach (var e in st.EnumerateArray())
                {
                    map[i.ToString()] = e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : e.ToString();
                    i++;
                }
                break;

            default:
                return null;
        }

        return map.Count > 0 ? map : null;
    }

    /// <summary>
    /// Liest eine Zahl, die als JSON-Number oder als String vorliegen kann (manche Adapter
    /// schreiben debounceTime als String). Fehlt das Feld oder ist es unlesbar, kommt null.
    /// </summary>
    private static long? ReadLong(JsonElement el, string property)
    {
        if (!el.TryGetProperty(property, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)) return n;
        if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out var s)) return s;
        return null;
    }

    private static ScriptInfo ParseScript(string id, JsonElement common, string name, bool enabled)
    {
        var engineType = common.TryGetProperty("engineType", out var et) && et.ValueKind == JsonValueKind.String
            ? et.GetString() ?? "" : "";

        var engine = engineType switch
        {
            var s when s.Equals("Blockly", StringComparison.OrdinalIgnoreCase) => ScriptEngine.Blockly,
            var s when s.StartsWith("TypeScript", StringComparison.OrdinalIgnoreCase) => ScriptEngine.TypeScript,
            var s when s.StartsWith("Javascript", StringComparison.OrdinalIgnoreCase) => ScriptEngine.JavaScript,
            var s when s.StartsWith("Rules", StringComparison.OrdinalIgnoreCase) => ScriptEngine.Blockly,
            _ => ScriptEngine.Unbekannt
        };

        var source = common.TryGetProperty("source", out var src) && src.ValueKind == JsonValueKind.String
            ? src.GetString() ?? "" : "";

        var decoded = BlocklyDecoder.Decode(source, engine == ScriptEngine.Blockly);

        SplitPath(id, out var folder, out var scriptName);
        // common.name gewinnt, wenn gesetzt — sonst das letzte ID-Segment.
        if (!string.IsNullOrWhiteSpace(name)) scriptName = name;

        return new ScriptInfo
        {
            Id = id,
            Name = scriptName,
            Folder = folder,
            Engine = engine,
            Enabled = enabled,
            Source = source,
            CleanSource = decoded.CleanSource,
            BlocklyXml = decoded.Xml,
            BlocklyBroken = decoded.Broken
        };
    }

    /// <summary>
    /// Zerlegt script.js.Ordner.Unterordner.Name in Ordnerpfad und Skriptnamen.
    /// Die Präfixe script.js.common. und script.js.global. werden entfernt.
    /// </summary>
    public static void SplitPath(string id, out string folder, out string name)
    {
        var rest = id;
        foreach (var prefix in new[] { "script.js.common.", "script.js.global.", "script.js." })
        {
            if (rest.StartsWith(prefix, StringComparison.Ordinal))
            {
                rest = rest[prefix.Length..];
                break;
            }
        }

        var parts = rest.Split('.');
        name = parts[^1];
        folder = parts.Length > 1 ? string.Join("/", parts[..^1]) : "";
    }

    /// <summary>
    /// common.name ist entweder ein String oder ein Übersetzungsobjekt
    /// ({"en": "…", "de": "…"}). Deutsch wird bevorzugt, dann Englisch.
    /// </summary>
    private static string ReadName(JsonElement common, string id)
    {
        if (!common.TryGetProperty("name", out var n)) return LastSegment(id);

        if (n.ValueKind == JsonValueKind.String)
            return n.GetString() ?? LastSegment(id);

        if (n.ValueKind == JsonValueKind.Object)
        {
            if (n.TryGetProperty("de", out var de) && de.ValueKind == JsonValueKind.String)
                return de.GetString() ?? LastSegment(id);
            if (n.TryGetProperty("en", out var en) && en.ValueKind == JsonValueKind.String)
                return en.GetString() ?? LastSegment(id);
        }

        return LastSegment(id);
    }

    private static string LastSegment(string id)
    {
        var i = id.LastIndexOf('.');
        return i >= 0 && i < id.Length - 1 ? id[(i + 1)..] : id;
    }
}

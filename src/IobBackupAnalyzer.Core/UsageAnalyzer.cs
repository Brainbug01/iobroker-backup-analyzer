namespace IobBackupAnalyzer.Core;

/// <summary>Wie ein Skript auf einen Datenpunkt zugreift.</summary>
public enum UsageAccess
{
    /// <summary>Die ID steht im Skript, der Zusammenhang gibt aber keine Richtung her.</summary>
    Unbekannt,
    /// <summary>Nur gelesen — getState, Trigger (on/subscribe).</summary>
    Liest,
    /// <summary>Geschrieben — setState und Verwandte, in Blockly der Steuern-Block.</summary>
    Schreibt,
    /// <summary>Beides im selben Skript.</summary>
    Beides
}

/// <summary>Woher eine Verwendung stammt.</summary>
public enum UsageSource
{
    /// <summary>Aus dem Quelltext eines Skripts.</summary>
    Skript,
    /// <summary>Aus der Konfiguration einer Adapter-Instanz (native).</summary>
    Adapter
}

/// <summary>Eine Verbindung zwischen einem Verwender (Skript oder Adapter) und einem Datenpunkt.</summary>
public sealed class UsageLink
{
    public UsageSource Source { get; init; } = UsageSource.Skript;

    /// <summary>Skript-ID oder Instanz (<c>shuttercontrol.0</c>).</summary>
    public required string SourceId { get; init; }

    /// <summary>Anzeigename: Skriptpfad oder Instanz.</summary>
    public required string SourceName { get; init; }

    /// <summary>false bei deaktiviertem Skript bzw. abgeschalteter Instanz.</summary>
    public bool SourceEnabled { get; init; }

    /// <summary>Nur bei Adaptern: Feldpfad in der Konfiguration.</summary>
    public string Field { get; init; } = "";

    public required string StateId { get; init; }

    /// <summary>Name des Datenpunkts aus dem Objekt; leer, wenn es kein Objekt (mehr) gibt.</summary>
    public string StateName { get; init; } = "";

    public UsageAccess Access { get; set; }

    /// <summary>Wie oft die ID im Skript vorkommt — grobes Maß dafür, wie zentral sie ist.</summary>
    public int Hits { get; set; }

    /// <summary>
    /// true, wenn die ID nicht wörtlich im Skript steht, sondern nur ihr Anfang: Das Skript
    /// setzt sie zur Laufzeit zusammen. Ein solcher Treffer ist ein Hinweis, kein Beweis.
    /// </summary>
    public bool Dynamic { get; set; }

    /// <summary>
    /// true, wenn die ID ausschließlich im Blockly-XML steht und nicht im erzeugten
    /// JavaScript. Das ist der Fingerabdruck eines deaktivierten Blocks: Er liegt im
    /// Skript, läuft aber nicht mit.
    /// </summary>
    public bool OnlyInXml { get; set; }

    public string AccessText => Source == UsageSource.Adapter
        // Ob ein Adapter seinen konfigurierten Datenpunkt liest oder schreibt, steht
        // nirgends im Backup — das weiß nur der Adapter selbst.
        ? "—"
        : Access switch
        {
            UsageAccess.Liest => "liest",
            UsageAccess.Schreibt => "schreibt",
            UsageAccess.Beides => "liest + schreibt",
            _ => "erwähnt"
        };

    /// <summary>„Skript" oder „Adapter" — die Art des Verwenders.</summary>
    public string SourceText => Source == UsageSource.Adapter ? "Adapter" : "Skript";

    /// <summary>Kurzer Klartext dazu, wie sicher der Fund ist.</summary>
    public string HintText =>
        Source == UsageSource.Adapter ? Field
        : OnlyInXml ? "nur im Blockly-XML (inaktiver Block?)"
        : Dynamic ? "ID zur Laufzeit zusammengesetzt"
        : Hits > 1 ? $"{Hits}× im Code"
        : "im Code";

    public string StatusText => SourceEnabled
        ? "Aktiv"
        : Source == UsageSource.Adapter ? "Instanz aus" : "Deaktiviert";
}

/// <summary>Ein Skript mit allen Datenpunkten, die es anfasst.</summary>
public sealed class ScriptUsage
{
    public required string ScriptId { get; init; }
    public required string DisplayPath { get; init; }
    public required string EngineText { get; init; }
    public bool Enabled { get; init; }

    public List<UsageLink> Links { get; init; } = new();

    public int StateCount => Links.Count;
    public int ReadCount => Links.Count(l => l.Access is UsageAccess.Liest or UsageAccess.Beides);
    public int WriteCount => Links.Count(l => l.Access is UsageAccess.Schreibt or UsageAccess.Beides);

    public string StatusText => Enabled ? "Aktiv" : "Deaktiviert";
}

/// <summary>Ein Datenpunkt mit allen Skripten, die ihn anfassen.</summary>
public sealed class StateUsage
{
    public required string Id { get; init; }
    public string Name { get; init; } = "";

    /// <summary>false bei einer Werte-Leiche: In der States-DB steht ein Wert, das Objekt fehlt.</summary>
    public bool ObjectExists { get; init; } = true;

    public bool IsAlias { get; init; }

    /// <summary>Ziel des Alias, falls es eins ist — sonst leer.</summary>
    public string AliasTarget { get; init; } = "";

    /// <summary>
    /// Letzte Wertänderung aus der States-Datenbank; null, wenn es dort keinen Eintrag gibt.
    /// Das ist die Gegenprobe zu einem Adapter- oder Skriptfund: Eine Konfigurationszeile
    /// sagt nur, dass die ID eingetragen ist — ob überhaupt noch etwas passiert, sagt der
    /// Zeitstempel (Rückfrage aus der Praxis zu einem längst nicht mehr genutzten Eintrag).
    /// </summary>
    public DateTime? LastChange { get; init; }

    /// <summary>Alter der letzten Änderung in Tagen, bezogen auf den Backup-Zeitpunkt.</summary>
    public int? AgeDays { get; init; }

    /// <summary>
    /// Ein Alias hat systembedingt keinen eigenen Eintrag in der States-Datenbank — der
    /// js-controller reicht auf das Ziel durch. „Kein Wert" ist dort also kein Befund.
    /// </summary>
    public bool HasState { get; init; }

    public string LastChangeText =>
        IsAlias ? "—  (Alias)"
        : !HasState ? "kein Wert"
        : LastChange is null ? "unbekannt"
        : AgeDays is null ? LastChange.Value.ToString("dd.MM.yyyy")
        : $"{LastChange.Value:dd.MM.yyyy}  ({AgeDays} T)";

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

    /// <summary>Alle Verwender: Skripte und Adapter-Instanzen gemeinsam.</summary>
    public List<UsageLink> Links { get; init; } = new();

    public int ScriptCount => Links.Count(l => l.Source == UsageSource.Skript);

    /// <summary>Adapter-Instanzen, die den Datenpunkt in ihrer Konfiguration nennen.</summary>
    public int AdapterCount => Links.Count(l => l.Source == UsageSource.Adapter);

    public int Readers => Links.Count(l => l.Access is UsageAccess.Liest or UsageAccess.Beides);
    public int Writers => Links.Count(l => l.Access is UsageAccess.Schreibt or UsageAccess.Beides);

    /// <summary>
    /// Der Fall, wegen dem diese Auswertung entstanden ist: Zwei Skripte schreiben
    /// denselben Datenpunkt und arbeiten dabei gegeneinander.
    /// </summary>
    public bool MultipleWriters => Writers > 1;

    /// <summary>Weder ein Skript noch eine Adapter-Konfiguration erwähnt den Datenpunkt.</summary>
    public bool Unused => Links.Count == 0;

    /// <summary>
    /// Kein Skript, aber eine Adapter-Konfiguration — der Fall, der ohne die zweite Quelle
    /// wie eine Karteileiche aussah (Shuttercontrol und Verwandte).
    /// </summary>
    public bool OnlyInAdapter => ScriptCount == 0 && AdapterCount > 0;

    public string KindText => IsAlias ? "Alias" : ObjectExists ? "Datenpunkt" : "Wert ohne Objekt";

    /// <summary>Alle Verwender als eine Zeile — für Tabellenspalte und CSV.</summary>
    public string ScriptsText => string.Join(", ", Links.Select(l => l.SourceName));
}

/// <summary>Ergebnis der Verwendungsanalyse, beide Richtungen.</summary>
public sealed class UsageReport
{
    /// <summary>Alle Skripte — auch die, die keinen Datenpunkt anfassen.</summary>
    public List<ScriptUsage> Scripts { get; init; } = new();

    /// <summary>Die betrachteten Datenpunkte (siehe <see cref="UsageAnalyzer"/>).</summary>
    public List<StateUsage> States { get; init; } = new();

    public int ScriptsTotal { get; init; }
    public int ScriptsWithStates { get; init; }

    /// <summary>Adapter-Instanzen, die mindestens einen Datenpunkt in ihrer Konfiguration nennen.</summary>
    public int AdaptersWithStates { get; init; }

    /// <summary>
    /// false, wenn das Backup keine Adapter-Konfigurationen mitbringt. Dann ist „kein
    /// Adapter-Verweis" keine Aussage, sondern eine fehlende Quelle.
    /// </summary>
    public bool HasAdapterConfig { get; init; }

    /// <summary>Wie viele Datenpunkt-IDs überhaupt gegen die Skripte geprüft wurden.</summary>
    public int StatesChecked { get; init; }

    public int StatesUsed => States.Count(s => !s.Unused);
    public int StatesUnused => States.Count(s => s.Unused);
    public int StatesMultiWriter => States.Count(s => s.MultipleWriters);
    public int AliasesUnused => States.Count(s => s.IsAlias && s.Unused);

    /// <summary>Datenpunkte, die mindestens eine Adapter-Instanz in ihrer Konfiguration nennt.</summary>
    public int StatesInAdapter => States.Count(s => s.AdapterCount > 0);

    /// <summary>Datenpunkte, die kein Skript, aber ein Adapter verwendet.</summary>
    public int StatesOnlyInAdapter => States.Count(s => s.OnlyInAdapter);

    /// <summary>Verbindungen aus Skripten — die Zeilen der Skript-Detailtabelle.</summary>
    public int Links => Scripts.Sum(s => s.Links.Count);

    /// <summary>
    /// Verbindungen aus Adapter-Konfigurationen. Sie erscheinen nur in der Datenpunkt-Sicht:
    /// Die Skript-Sicht listet Skripte, und ein Adapter ist keines.
    /// </summary>
    public int AdapterLinks => States.Sum(s => s.AdapterCount);
}

/// <summary>
/// Kreuzreferenz zwischen Skripten und Datenpunkten: Welches Skript benutzt welchen State —
/// und umgekehrt, welche Skripte hängen an einem State.
///
/// <b>Wozu?</b> Zwei Fragen, die sich aus einem Backup sonst nur mühsam beantworten lassen:
/// „Warum ändert sich dieser Wert von allein?" — meist, weil ein zweites, längst vergessenes
/// Skript ihn ebenfalls schreibt. Und: „Was ist von dem, was ich angelegt habe, nie benutzt
/// worden?" — vor allem Aliasse, die man einmal gebaut und dann nie in ein Skript gesetzt hat.
///
/// <b>Wie gesucht wird.</b> Nicht jede ID im ganzen Code, sondern umgekehrt: Aus jedem Skript
/// werden die Zeichenketten-Literale herausgelöst (bei Blockly zusätzlich die Feldinhalte des
/// XML) und gegen den Objektbestand geschlagen. Das ist genau, schnell und kommt auch mit IDs
/// zurecht, die Leerzeichen oder Sonderzeichen enthalten — ein reiner Wortscanner würde
/// „0_userdata.0.Mein Datenpunkt" mitten im Namen zerschneiden.
///
/// <b>Grundlage ist das erzeugte JavaScript</b>, auch bei Blockly: Der Adapter übersetzt jeden
/// Block in <c>setState</c>/<c>getState</c>, und daran ist die Richtung des Zugriffs ablesbar.
/// Das XML wird zusätzlich durchsucht — was nur dort auftaucht, gehört zu einem deaktivierten
/// Block und wird als solcher ausgewiesen.
///
/// <b>Grenzen, die bleiben.</b> Setzt ein Skript die ID zur Laufzeit zusammen
/// (<c>'shelly.0.' + name</c>), ist der genaue Datenpunkt nicht bestimmbar; solche Funde
/// erscheinen über den erkannten Anfang und sind als „zur Laufzeit zusammengesetzt"
/// gekennzeichnet. Nutzung außerhalb der Skripte — VIS, Adapter, externe Systeme — sieht diese
/// Analyse grundsätzlich nicht; dafür sind die Tabs „VIS-Datenpunkte" und „Verwaiste
/// Datenpunkte" zuständig.
/// </summary>
public static class UsageAnalyzer
{
    /// <summary>
    /// Ein Präfix muss mindestens so lang sein, um als dynamischer Treffer zu gelten.
    /// „0_userdata.0" träfe sonst auf alles zu und wäre als Aussage wertlos.
    /// </summary>
    private const int MinPrefixLength = 15;

    /// <summary>
    /// Wie viele Datenpunkte ein Präfix höchstens aufspannen darf. Wer <c>'javascript.0.'</c>
    /// zusammensetzt, meint nicht jeden Datenpunkt darunter — ein derart weiter Treffer
    /// sagt nichts und würde die Tabelle fluten.
    /// </summary>
    private const int MaxPrefixMatches = 25;

    /// <summary>Zeichen vor der Fundstelle, in denen nach dem Zugriffsbefehl gesucht wird.</summary>
    private const int ContextWindow = 140;

    /// <summary>
    /// Im XML braucht es mehr Vorlauf: Zwischen dem öffnenden Block und dem Feld mit der ID
    /// stehen noch Block-Kennung und ein eingeschachtelter Feldblock.
    /// </summary>
    private const int XmlContextWindow = 400;

    private static readonly (string Word, UsageAccess Access)[] Keywords =
    {
        // Schreibend. "setState" deckt setStateAsync, setStateDelayed und setStateChanged
        // gleich mit ab, weil es deren gemeinsamer Anfang ist.
        ("setState", UsageAccess.Schreibt),
        ("createState", UsageAccess.Schreibt),
        ("deleteState", UsageAccess.Schreibt),
        ("createAlias", UsageAccess.Schreibt),

        // Lesend. Ein Trigger liest ebenfalls — er reagiert auf den Wert, ändert ihn nicht.
        ("getState", UsageAccess.Liest),
        ("existsState", UsageAccess.Liest),
        ("getHistory", UsageAccess.Liest),
        ("getObject", UsageAccess.Liest),
        ("subscribe", UsageAccess.Liest),
        ("on(", UsageAccess.Liest)
    };

    /// <summary>
    /// Blockly-Blocktypen. Verglichen wird das letzte <c>type="…"</c> vor der Fundstelle —
    /// das ist der Block, zu dem das Feld gehört.
    /// </summary>
    private static UsageAccess FromBlockType(string type)
    {
        if (type.Contains("control", StringComparison.Ordinal)
            || type.Contains("update", StringComparison.Ordinal)
            || type.Contains("create", StringComparison.Ordinal)) return UsageAccess.Schreibt;

        if (type.Contains("get", StringComparison.Ordinal)
            || type.StartsWith("on", StringComparison.Ordinal)
            || type.Contains("trigger", StringComparison.Ordinal)) return UsageAccess.Liest;

        return UsageAccess.Unbekannt;
    }

    public static UsageReport Analyze(BackupData data, CancellationToken ct = default)
    {
        // ---------- Was überhaupt gesucht wird ----------
        var known = new Dictionary<string, StateUsage>(StringComparer.Ordinal);

        // Bezugspunkt für das Alter ist der Backup-Zeitpunkt, nicht „heute" — sonst ließe
        // ein altes Backup jeden Datenpunkt künstlich tot erscheinen.
        var reference = data.CreatedAt ?? DateTime.Now;

        foreach (var o in data.Objects)
        {
            if (o.Type != "state") continue;

            var hasState = data.States.TryGetValue(o.Id, out var st);
            var lastChange = hasState ? st!.LastChange : null;

            known[o.Id] = new StateUsage
            {
                Id = o.Id,
                Name = o.Name,
                ObjectExists = true,
                IsAlias = o.Id.StartsWith("alias.", StringComparison.OrdinalIgnoreCase)
                          || o.AliasTarget is not null,
                AliasTarget = o.AliasTarget ?? "",
                HasState = hasState,
                LastChange = lastChange,
                AgeDays = lastChange is null ? null : (int)(reference - lastChange.Value).TotalDays,
                Val = hasState ? st!.Val : "",
                HasVal = hasState && st!.HasVal,
                ValTruncated = hasState && st!.ValTruncated,
                ValLength = hasState ? st!.ValLength : 0
            };
        }

        // Werte ohne Objekt gehören dazu: Schreibt ein Skript auf eine ID, zu der es kein
        // Objekt mehr gibt, ist genau das der interessante Befund.
        foreach (var id in data.States.Keys)
        {
            if (known.ContainsKey(id)) continue;

            var state = data.States[id];
            var lastChange = state.LastChange;
            known[id] = new StateUsage
            {
                Id = id,
                ObjectExists = false,
                HasState = true,
                LastChange = lastChange,
                AgeDays = lastChange is null ? null : (int)(reference - lastChange.Value).TotalDays,
                Val = state.Val,
                HasVal = state.HasVal,
                ValTruncated = state.ValTruncated,
                ValLength = state.ValLength
            };
        }

        // Sortiert für die Präfixsuche — BinarySearch braucht dieselbe Ordnung wie der
        // Vergleicher, deshalb durchgehend Ordinal.
        var sortedIds = known.Keys.ToArray();
        Array.Sort(sortedIds, StringComparer.Ordinal);

        // ---------- Skripte durchgehen ----------
        var scripts = new List<ScriptUsage>(data.Scripts.Count);

        foreach (var s in data.Scripts)
        {
            ct.ThrowIfCancellationRequested();

            var usage = new ScriptUsage
            {
                ScriptId = s.Id,
                DisplayPath = s.DisplayPath,
                EngineText = s.EngineText,
                Enabled = s.Enabled
            };

            // Je Datenpunkt genau eine Verbindung, auch wenn die ID mehrfach vorkommt.
            var found = new Dictionary<string, UsageLink>(StringComparer.Ordinal);

            ScanText(s.CleanSource, isXml: false, s, known, sortedIds, found, ct);

            if (s.BlocklyXml is not null)
                ScanText(s.BlocklyXml, isXml: true, s, known, sortedIds, found, ct);

            foreach (var link in found.Values.OrderBy(l => l.StateId, StringComparer.OrdinalIgnoreCase))
            {
                usage.Links.Add(link);
                known[link.StateId].Links.Add(link);
            }

            scripts.Add(usage);
        }

        // ---------- zweite Quelle: die Adapter-Konfigurationen ----------
        // Viele Adapter bekommen ihre Datenpunkte in der Instanzkonfiguration genannt
        // (Shuttercontrol seine Rollläden, awtrix-light die Werte seiner Apps). Ohne diese
        // Quelle stünde ein solcher Datenpunkt hier bei null — und damit fälschlich in der
        // Liste der nie verwendeten. Der Abgleich mit dem Objektbestand ist im Loader
        // bereits passiert; hier kommen nur bestätigte Verweise an.
        var adapters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in data.AdapterRefs)
        {
            ct.ThrowIfCancellationRequested();
            if (!known.TryGetValue(r.StateId, out var state)) continue;

            adapters.Add(r.Instance);
            state.Links.Add(new UsageLink
            {
                Source = UsageSource.Adapter,
                SourceId = r.Instance,
                SourceName = r.Instance,
                SourceEnabled = r.InstanceEnabled,
                Field = r.Where,
                StateId = r.StateId,
                StateName = state.Name,
                Access = UsageAccess.Unbekannt,
                Hits = 1
            });
        }

        // ---------- Welche Datenpunkte in der Gegenrichtung gezeigt werden ----------
        // Alle wären zehntausende Zeilen, von denen die allermeisten nichts mit Skripten zu
        // tun haben. Gezeigt wird deshalb, was hier eine Aussage trägt: alles Benutzte, dazu
        // Aliasse und eigene Datenpunkte auch dann, wenn sie unbenutzt sind — gerade deren
        // Fehlen ist ja der gesuchte Befund.
        var states = known.Values
            .Where(s => s.Links.Count > 0 || s.IsAlias || IsOwnDatapoint(s.Id))
            .OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new UsageReport
        {
            Scripts = scripts.OrderBy(s => s.DisplayPath, StringComparer.OrdinalIgnoreCase).ToList(),
            States = states,
            ScriptsTotal = data.Scripts.Count,
            ScriptsWithStates = scripts.Count(s => s.Links.Count > 0),
            AdaptersWithStates = adapters.Count,
            HasAdapterConfig = data.HasAdapterConfig,
            StatesChecked = known.Count
        };
    }

    /// <summary>
    /// Datenpunkte, die der Nutzer selbst angelegt hat. Nur für sie ist „wird nirgends
    /// verwendet" eine Aussage — bei Adapter-Datenpunkten ist es der Normalfall.
    /// </summary>
    private static bool IsOwnDatapoint(string id) =>
        id.StartsWith("0_userdata.", StringComparison.OrdinalIgnoreCase)
        || id.StartsWith("javascript.", StringComparison.OrdinalIgnoreCase);

    // ------------------------------------------------------------------ Textsuche

    private static void ScanText(string text, bool isXml, ScriptInfo script,
                                 Dictionary<string, StateUsage> known, string[] sortedIds,
                                 Dictionary<string, UsageLink> found, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(text)) return;

        foreach (var (value, start) in Literals(text, isXml))
        {
            ct.ThrowIfCancellationRequested();
            if (value.Length < 5 || !value.Contains('.')) continue;

            if (known.TryGetValue(value, out var state))
            {
                Add(found, script, state, DetermineAccess(text, start, isXml), dynamic: false, isXml);
                continue;
            }

            // Kein wörtlicher Treffer: Vielleicht ist es der Anfang einer zusammengesetzten
            // ID — `'0_userdata.0.Heizung.' + raum`. Im Literal steht dann nur der feste Teil.
            var prefix = PrefixPart(value);
            if (prefix.Length < MinPrefixLength) continue;

            var matches = ByPrefix(sortedIds, prefix);
            if (matches.Count is 0 or > MaxPrefixMatches) continue;

            var access = DetermineAccess(text, start, isXml);
            foreach (var id in matches)
                Add(found, script, known[id], access, dynamic: true, isXml);
        }
    }

    private static void Add(Dictionary<string, UsageLink> found, ScriptInfo script,
                            StateUsage state, UsageAccess access, bool dynamic, bool isXml)
    {
        if (!found.TryGetValue(state.Id, out var link))
        {
            link = new UsageLink
            {
                Source = UsageSource.Skript,
                SourceId = script.Id,
                SourceName = script.DisplayPath,
                SourceEnabled = script.Enabled,
                StateId = state.Id,
                StateName = state.Name,
                Access = access,
                Dynamic = dynamic,
                OnlyInXml = isXml,
                Hits = 1
            };
            found[state.Id] = link;
            return;
        }

        link.Hits++;

        // Ein wörtlicher Fund sticht den geratenen; ein Fund im erzeugten Code sticht den
        // aus dem XML.
        if (!dynamic) link.Dynamic = false;
        if (!isXml) link.OnlyInXml = false;

        link.Access = Combine(link.Access, access);
    }

    private static UsageAccess Combine(UsageAccess a, UsageAccess b)
    {
        if (a == b) return a;
        if (a == UsageAccess.Unbekannt) return b;
        if (b == UsageAccess.Unbekannt) return a;
        return UsageAccess.Beides;
    }

    /// <summary>
    /// Der feste Anfang eines Literals: alles vor der ersten Einsetzstelle eines
    /// Template-Strings, ohne abschließenden Punkt.
    /// </summary>
    private static string PrefixPart(string value)
    {
        var cut = value.IndexOf("${", StringComparison.Ordinal);
        var prefix = cut >= 0 ? value[..cut] : value;
        return prefix.TrimEnd('.');
    }

    /// <summary>Alle bekannten IDs, die mit <paramref name="prefix"/> beginnen.</summary>
    private static List<string> ByPrefix(string[] sortedIds, string prefix)
    {
        var result = new List<string>();

        var index = Array.BinarySearch(sortedIds, prefix, StringComparer.Ordinal);
        if (index < 0) index = ~index;

        for (var i = index; i < sortedIds.Length; i++)
        {
            if (!sortedIds[i].StartsWith(prefix, StringComparison.Ordinal)) break;

            // Der Präfix muss an einer Ebenengrenze enden, sonst würde „…Heizung" auch
            // „…Heizungspumpe" einsammeln.
            if (sortedIds[i].Length > prefix.Length && sortedIds[i][prefix.Length] != '.') continue;

            result.Add(sortedIds[i]);
            if (result.Count > MaxPrefixMatches) break;
        }

        return result;
    }

    /// <summary>
    /// Alle Zeichenketten-Literale eines Skripts samt ihrer Position. Bei XML sind es die
    /// Textinhalte der Elemente — dort stehen die Datenpunkte der Blockly-Felder.
    /// </summary>
    private static IEnumerable<(string Value, int Start)> Literals(string text, bool isXml)
    {
        if (isXml)
        {
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] != '>') continue;

                var end = text.IndexOf('<', i + 1);
                if (end < 0) yield break;

                var value = text[(i + 1)..end].Trim();
                if (value.Length > 0) yield return (value, i + 1);

                i = end - 1;
            }
            yield break;
        }

        for (var i = 0; i < text.Length; i++)
        {
            var quote = text[i];
            if (quote is not ('"' or '\'' or '`')) continue;

            var start = i + 1;
            var j = start;

            while (j < text.Length)
            {
                if (text[j] == '\\') { j += 2; continue; }
                if (text[j] == quote) break;
                // Einfache und doppelte Anführungszeichen enden spätestens am Zeilenende;
                // ohne diese Bremse würde ein einzelnes Apostroph im Kommentar („don't")
                // den Rest der Datei als ein Literal verschlucken.
                if (quote != '`' && text[j] == '\n') break;
                j++;
            }

            if (j >= text.Length) yield break;

            if (text[j] == quote && j > start)
                yield return (text[start..j], start);

            i = j;
        }
    }

    /// <summary>
    /// Die Zugriffsart aus dem Text vor der Fundstelle. Maßgeblich ist das <em>letzte</em>
    /// Schlüsselwort davor: In <c>setState('a', getState('b'))</c> gehört „b" zu getState
    /// und „a" zu setState — genau in dieser Reihenfolge stehen sie im Text.
    /// </summary>
    private static UsageAccess DetermineAccess(string text, int start, bool isXml)
    {
        var from = Math.Max(0, start - (isXml ? XmlContextWindow : ContextWindow));
        var window = text[from..start];

        if (isXml)
        {
            // Rückwärts durch die Blocktypen, bis einer die Richtung hergibt. Der nächste
            // ist oft nur ein Feldblock: Die Datenpunkt-Auswahl steckt regelmäßig in einem
            // <block type="field_oid">, das seinerseits in dem Block liegt, auf den es
            // ankommt (control = schreiben, get_value = lesen).
            var searchTo = window.Length - 1;

            while (searchTo >= 0)
            {
                var marker = window.LastIndexOf("type=\"", searchTo, StringComparison.Ordinal);
                if (marker < 0) return UsageAccess.Unbekannt;

                var typeStart = marker + 6;
                var typeEnd = window.IndexOf('"', typeStart);
                if (typeEnd < 0) return UsageAccess.Unbekannt;

                var fromBlock = FromBlockType(window[typeStart..typeEnd]);
                if (fromBlock != UsageAccess.Unbekannt) return fromBlock;

                searchTo = marker - 1;
            }

            return UsageAccess.Unbekannt;
        }

        var best = -1;
        var access = UsageAccess.Unbekannt;

        foreach (var (word, kind) in Keywords)
        {
            var pos = window.LastIndexOf(word, StringComparison.Ordinal);
            if (pos <= best) continue;

            // Wortgrenze: „function(" enthält „on(", „mySetState" enthält „setState".
            // Ein Punkt davor ist dagegen in Ordnung — adapter.setState() ist gemeint.
            if (pos > 0 && (char.IsLetterOrDigit(window[pos - 1]) || window[pos - 1] == '_')) continue;

            best = pos;
            access = kind;
        }

        return access;
    }
}

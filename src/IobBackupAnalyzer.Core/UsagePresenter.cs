namespace IobBackupAnalyzer.Core;

/// <summary>Die beiden Blickrichtungen des Tabs „Verwendung".</summary>
public enum UsageDirection
{
    /// <summary>Ein Skript oben, seine Datenpunkte unten.</summary>
    ByScript,
    /// <summary>Ein Datenpunkt oben, die Skripte dazu unten.</summary>
    ByState,
    /// <summary>
    /// IDs aus Skripten, zu denen es kein Objekt gibt — die Gegenrichtung der beiden
    /// anderen Sichten: nicht wer benutzt was, sondern wer greift ins Leere.
    /// </summary>
    DeadRefs
}

/// <summary>Vorfilter der Datenpunkt-Sicht — die Fragen, die man hier wirklich stellt.</summary>
public enum UsageStateFilter
{
    /// <summary>Alles, was die Analyse betrachtet hat.</summary>
    Alle,
    /// <summary>Nur Datenpunkte, die ein Skript oder ein Adapter verwendet.</summary>
    NurVerwendet,
    /// <summary>Von mehr als einem Skript beschrieben — die Ursache unerklärlicher Werte.</summary>
    MehrfachBeschrieben,
    /// <summary>
    /// In mindestens einer Adapter-Konfiguration eingetragen, unabhängig davon, ob auch
    /// Skripte darauf zugreifen. Das ist die Frage „welche Datenpunkte hat sich ein Adapter
    /// eingetragen?" — sie will das ganze Bild, nicht nur die skriptlosen Fälle.
    /// </summary>
    ImAdapter,
    /// <summary>
    /// Im Adapter eingetragen und in keinem Skript — die Datenpunkte, die ohne diese
    /// Auswertung wie Karteileichen aussähen.
    /// </summary>
    NurImAdapter,
    /// <summary>Weder Skript noch Adapter — Aliasse und eigene Datenpunkte auf Verdacht.</summary>
    NirgendsVerwendet
}

/// <summary>
/// UI-neutrale Logik des Tabs „Verwendung": Spalten, Filter, Zähltexte und Hervorhebungen
/// für beide Blickrichtungen. Geteilt von WinForms- und Avalonia-Oberfläche.
/// </summary>
public static class UsagePresenter
{
    public const string Warning =
        "Gesucht wird in den Skripten (JavaScript und Blockly-XML) und in den " +
        "Adapter-Konfigurationen. Ein Adapter-Treffer heißt: Die ID steht dort eingetragen — " +
        "ob die Funktion auch benutzt wird, sagt das Backup nicht; alte Einträge bleiben " +
        "stehen. Nutzung in VIS oder in externen Systemen sieht diese Auswertung gar nicht. " +
        "Setzt ein Skript IDs zur Laufzeit zusammen, steht der Fund als „zusammengesetzt\" da " +
        "und ist ein Hinweis, kein Beweis.";

    /// <summary>
    /// Steht als Hinweis an beiden Tabellen. Der Sprung ist sonst nicht zu erraten — und
    /// genau er beantwortet die Anschlussfrage „und was steht in dem Skript drin?".
    /// </summary>
    public const string JumpHint =
        "Doppelklick auf ein Skript öffnet es im Tab „Skripte\" mit seinem Quelltext.";

    public static readonly string[] ViewLabels =
        { "Skript → Datenpunkte", "Datenpunkt → Skripte", "Verweise ins Leere" };

    /// <summary>
    /// Farblegende der Datenpunkt-Sicht: erst die Befunde, dann der Normalfall.
    ///
    /// Der unauffällige Fall steht bewusst mit dabei. Eine Legende, die nur die drei
    /// hervorgehobenen Fälle nennt, lässt offen, ob die vierte Farbe auch etwas bedeutet.
    ///
    /// <b>Farbnamen stehen absichtlich nicht im Text</b>, sondern nur das Farbquadrat: Im
    /// dunklen Thema ist die unauffällige Schrift weiß, nicht schwarz — eine Legende, die
    /// „Schwarz" behauptet, wäre dort schlicht falsch. Das Quadrat trägt die jeweilige
    /// Farbe und stimmt in beiden Themen.
    /// </summary>
    public static readonly (RowEmphasis Emphasis, string Text)[] StateLegend =
    {
        (RowEmphasis.Problem, "■ von mehreren Skripten beschrieben"),
        (RowEmphasis.Warn, "■ Wert ohne Objekt"),
        (RowEmphasis.Muted, "■ in keinem Skript verwendet"),
        (RowEmphasis.None, "■ unauffällig — höchstens ein Skript schreibt")
    };

    /// <summary>Legende der Skript-Sicht — dort gibt es nur den einen Befund.</summary>
    public static readonly (RowEmphasis Emphasis, string Text)[] ScriptLegend =
    {
        (RowEmphasis.Muted, "■ Skript ist deaktiviert oder fasst keinen Datenpunkt an"),
        (RowEmphasis.None, "■ aktives Skript mit Datenpunkt-Bezug")
    };

    public static readonly string[] StateFilterLabels =
        { "Alle betrachteten Datenpunkte",
          "Nur verwendete",
          "Von mehreren Skripten beschrieben",
          "In einem Adapter eingetragen",
          "Im Adapter, aber in keinem Skript",
          "Nirgends verwendet" };

    // ------------------------------------------------------------------ Sicht: Skript → States

    public static readonly string[] ColumnsScripts =
        { "Skript", "Typ", "Status", "Datenpunkte", "liest", "schreibt" };

    public static readonly string[] ColumnsScriptDetail =
        { "Datenpunkt-ID", "Name", "Zugriff", "Fundstelle", "Art" };

    public static List<ScriptUsage> FilterScripts(IEnumerable<ScriptUsage> all, bool onlyWithStates,
                                                  string? term)
    {
        var q = onlyWithStates ? all.Where(s => s.StateCount > 0) : all;

        var t = (term ?? "").Trim();
        if (t.Length > 0)
            // Auch die Datenpunkte durchsuchen: Wer eine ID eintippt, sucht das Skript dazu.
            q = q.Where(s => s.DisplayPath.Contains(t, StringComparison.OrdinalIgnoreCase)
                          || s.Links.Any(l => l.StateId.Contains(t, StringComparison.OrdinalIgnoreCase)));

        return q.ToList();
    }

    public static string[] RowScript(ScriptUsage s) => new[]
    {
        s.DisplayPath, s.EngineText, s.StatusText,
        s.StateCount.ToString(), s.ReadCount.ToString(), s.WriteCount.ToString()
    };

    /// <summary>Ein Skript ohne jeden Datenpunkt-Bezug ist kein Befund, nur unauffällig.</summary>
    public static RowEmphasis EmphasisScript(ScriptUsage s) =>
        !s.Enabled ? RowEmphasis.Muted
        : s.StateCount == 0 ? RowEmphasis.Muted
        : RowEmphasis.None;

    public static string[] RowScriptDetail(UsageLink l) => new[]
    {
        l.StateId, l.StateName, l.AccessText, l.HintText, l.SourceEnabled ? "" : "Skript deaktiviert"
    };

    public static string CountScripts(int shown, UsageReport report) =>
        shown == report.Scripts.Count
            ? $"{report.ScriptsWithStates:N0} von {report.ScriptsTotal:N0} Skripten verwenden Datenpunkte · " +
              $"{report.Links:N0} Verbindungen"
            : $"{shown:N0} von {report.ScriptsTotal:N0} Skripten";

    // ------------------------------------------------------------------ Sicht: State → Skripte

    public static readonly string[] ColumnsStates =
        { "Datenpunkt-ID", "Name", "Art", "Skripte", "Adapter", "liest", "schreibt",
          "Zuletzt geändert" };

    public static readonly string[] ColumnsStateDetail =
        { "Art", "Verwender", "Zugriff", "Fundstelle", "Status" };

    /// <summary>Wie <see cref="ColumnsStates"/>, zusätzlich mit allen Verwendern in einer Spalte.</summary>
    public static readonly string[] CsvColumnsStates =
        { "Datenpunkt-ID", "Name", "Art", "Skripte", "Adapter", "liest", "schreibt",
          "Zuletzt geändert", "Alter (Tage)", "Verwendet in" };

    public static List<StateUsage> FilterStates(IEnumerable<StateUsage> all, UsageStateFilter filter,
                                                string? term)
    {
        var q = filter switch
        {
            UsageStateFilter.NurVerwendet => all.Where(s => !s.Unused),
            UsageStateFilter.MehrfachBeschrieben => all.Where(s => s.MultipleWriters),
            UsageStateFilter.ImAdapter => all.Where(s => s.AdapterCount > 0),
            UsageStateFilter.NurImAdapter => all.Where(s => s.OnlyInAdapter),
            UsageStateFilter.NirgendsVerwendet => all.Where(s => s.Unused),
            _ => all
        };

        var t = (term ?? "").Trim();
        if (t.Length > 0)
            q = q.Where(s => s.Id.Contains(t, StringComparison.OrdinalIgnoreCase)
                          || s.Name.Contains(t, StringComparison.OrdinalIgnoreCase)
                          || s.Links.Any(l => l.SourceName.Contains(t, StringComparison.OrdinalIgnoreCase)));

        return q.ToList();
    }

    public static string[] RowState(StateUsage s) => new[]
    {
        s.Id, s.Name, s.KindText,
        s.ScriptCount.ToString(), s.AdapterCount.ToString(),
        s.Readers.ToString(), s.Writers.ToString(), s.LastChangeText
    };

    public static string[] CsvRowState(StateUsage s) => new[]
    {
        s.Id, s.Name, s.KindText,
        s.ScriptCount.ToString(), s.AdapterCount.ToString(),
        s.Readers.ToString(), s.Writers.ToString(),
        s.LastChange?.ToString("dd.MM.yyyy HH:mm") ?? "", s.AgeDays?.ToString() ?? "",
        s.ScriptsText
    };

    /// <summary>
    /// Zwei Befunde verdienen Farbe: mehrere Schreiber (dort entstehen die unerklärlichen
    /// Werte) und ein Wert, zu dem das Objekt fehlt. Unbenutztes bleibt gedämpft — es ist
    /// eine Frage, kein Fehler.
    /// </summary>
    public static RowEmphasis EmphasisState(StateUsage s) =>
        s.MultipleWriters ? RowEmphasis.Problem
        : !s.ObjectExists ? RowEmphasis.Warn
        : s.Unused ? RowEmphasis.Muted
        : RowEmphasis.None;

    public static string[] RowStateDetail(UsageLink l) => new[]
    {
        l.SourceText, l.SourceName, l.AccessText, l.HintText, l.StatusText
    };

    public static string CountStates(int shown, int total, UsageReport report) =>
        shown == total
            ? $"{report.StatesUsed:N0} verwendet · {report.StatesMultiWriter:N0} von mehreren " +
              $"Skripten beschrieben · {report.StatesInAdapter:N0} in einem Adapter " +
              $"({report.StatesOnlyInAdapter:N0} davon ohne Skript) · " +
              $"{report.AliasesUnused:N0} Aliasse nirgends verwendet"
            : $"{shown:N0} von {total:N0} Datenpunkten";

    /// <summary>
    /// Kennzahlenzeile über beiden Sichten. Zeilentrenner ist <c>\n</c>, wie in den
    /// übrigen Presentern.
    /// </summary>
    public static string Stats(UsageReport report)
    {
        // Ohne Adapter-Konfigurationen fehlt die halbe Aussage — das gehört in die erste
        // Zeile, nicht ins Kleingedruckte. Benannt wird der Zustand, nicht seine Ursache:
        // Dass die native-Abschnitte fehlen, kann daran liegen, dass jemand sie vor dem
        // Weitergeben entfernt hat — das weiß das Backup selbst nicht.
        var adapter = report.HasAdapterConfig
            ? $"{report.AdaptersWithStates:N0} Adapter-Instanzen mit eingetragenen Datenpunkten"
            : "keine Adapter-Konfigurationen im Backup — diese Quelle fehlt hier ganz";

        return $"{report.ScriptsTotal:N0} Skripte · {report.ScriptsWithStates:N0} davon mit " +
               $"Datenpunkt-Bezug · {adapter}\n" +
               $"{report.Links:N0} Verbindungen zu {report.StatesUsed:N0} von " +
               $"{report.StatesChecked:N0} bekannten Datenpunkten · " +
               $"{report.StatesMultiWriter:N0} von mehr als einem Skript beschrieben";
    }

    /// <summary>Hinweis unter der Detailtabelle — nennt den gewählten Eintrag beim Namen.</summary>
    /// <summary>
    /// Überschrift der Detailtabelle. In der Datenpunkt-Sicht bewusst „Verwender" statt
    /// „Skripte": Dort stehen auch Adapter-Instanzen — und „verwenden" behauptet mehr, als
    /// eine Konfigurationszeile hergibt (Rückfrage aus der Praxis zu einem Adapter-Eintrag,
    /// den der Besitzer längst nicht mehr benutzt).
    /// </summary>
    public static string DetailTitle(UsageDirection view, string? selection) =>
        selection is null || selection.Length == 0
            ? view == UsageDirection.ByScript
                ? "Datenpunkte des Skripts — oben ein Skript auswählen"
                : "Verwender des Datenpunkts — oben einen Datenpunkt auswählen"
            : view == UsageDirection.ByScript
                ? $"Datenpunkte in „{selection}\""
                : $"Skripte und Adapter, die „{selection}\" nennen";

    public static string CsvName(UsageDirection view) =>
        view == UsageDirection.ByScript ? "skripte-datenpunkte.csv" : "datenpunkte-skripte.csv";

    // ------------------------------------------------------------------ Sicht: Verweise ins Leere

    public static readonly string[] ColumnsDeadRefs =
        { "Skript", "Status", "Datenpunkt-ID", "Befund" };

    public static string[] RowDeadRef(DeadRefRow r) =>
        new[] { r.ScriptName, r.StatusText, r.StateId, r.VerdachtText };

    /// <summary>
    /// Rot, wo der Verdacht belastbar ist: Den Namensraum gibt es, den Datenpunkt nicht.
    /// Fehlt der ganze Namensraum, bleibt die Zeile unauffällig — dort ist ebenso gut ein
    /// Skript für eine andere Anlage denkbar.
    /// </summary>
    public static RowEmphasis EmphasisDeadRef(DeadRefRow r) =>
        r.NamespaceExists ? RowEmphasis.Problem : RowEmphasis.None;

    public static readonly (RowEmphasis Emphasis, string Text)[] DeadRefLegend =
    {
        (RowEmphasis.Problem, "■ Adapter vorhanden, Datenpunkt fehlt"),
        (RowEmphasis.None, "■ Namensraum fehlt ganz")
    };

    /// <summary>
    /// Der Warntext dieser Sicht. Er nennt zwei Grenzen, ohne die die Liste falsch gelesen
    /// würde: Gesucht wird im erzeugten JavaScript — ein abgeschalteter Blockly-Baustein
    /// läuft nicht, seine Datenpunkte fehlen dann zu Recht. Und eine ID, die erst zur
    /// Laufzeit entsteht, ist nicht prüfbar.
    /// </summary>
    public const string DeadRefWarning =
        "Gesucht wird im erzeugten JavaScript, nicht im Blockly-XML: Ein abgeschalteter "
      + "Baustein läuft nicht, seine Datenpunkte fehlen dann zu Recht. Rot sind die "
      + "deutlichen Fälle — den Namensraum gibt es, den Datenpunkt nicht. Fehlt der "
      + "Namensraum ganz, kann es ebenso ein Skript für eine andere Anlage sein. IDs, die "
      + "ein Skript zur Laufzeit zusammensetzt, sind nicht erkennbar; über sie sagt diese "
      + "Liste nichts.";

    /// <summary>Kennzahlenzeile dieser Sicht — dieselbe Aufgabe wie <see cref="Stats"/>.</summary>
    public static string StatsDeadRefs(IReadOnlyList<DeadRefRow> rows)
    {
        var stark = rows.Count(r => r.NamespaceExists);
        var skripte = rows.Select(r => r.ScriptId).Distinct(StringComparer.Ordinal).Count();
        var aktiv = rows.Where(r => r.ScriptEnabled)
                        .Select(r => r.ScriptId).Distinct(StringComparer.Ordinal).Count();

        if (rows.Count == 0)
            return "Kein Skript greift auf einen Datenpunkt zu, den es im Backup nicht gibt.";

        return $"{rows.Count:N0} Verweise auf Datenpunkte, die es im Backup nicht gibt · "
             + $"{stark:N0} davon mit vorhandenem Namensraum · aus {skripte:N0} Skripten "
             + $"({aktiv:N0} davon aktiv)";
    }

    public static string CountDeadRefs(int shown) => $"{shown:N0} Verweise";
}

namespace IobBackupAnalyzer.Core;

/// <summary>
/// Wie stark eine Zeile hervorgehoben wird. Die konkrete Farbe wählt jede Oberfläche
/// selbst — Core bleibt frei von UI-Belangen.
/// </summary>
public enum RowEmphasis
{
    /// <summary>Normale Darstellung.</summary>
    None,
    /// <summary>Gedämpft — nur zur Einordnung mitgeführt, kein Befund.</summary>
    Muted,
    /// <summary>Auffällig, aber nicht eindeutig — verdient einen zweiten Blick.</summary>
    Warn,
    /// <summary>Der eigentliche Befund.</summary>
    Problem,
    /// <summary>Zuwachs / hinzugekommen — im Vergleich grün dargestellt.</summary>
    Positive
}

/// <summary>Die wählbaren Sichten auf states.jsonl (Analyse C).</summary>
public enum StateView
{
    /// <summary>Werte ohne Objekt — die Werte-Leichen.</summary>
    WithoutObject,
    /// <summary>Objekte, zu denen nie ein Wert geschrieben wurde.</summary>
    WithoutState,
    /// <summary>Alle States, nach letzter Änderung sortiert.</summary>
    Oldest,
    /// <summary>Qualitätscode ungleich „gut".</summary>
    BadQuality,
    /// <summary>Befehle ohne Quittung (ack = false).</summary>
    Unacknowledged
}

/// <summary>
/// UI-neutrale Logik des Tabs „Verwaiste Datenpunkte" mit seinen drei Analysen:
/// A — Objekte ohne Adapter-Instanz, B — unbenutzte User-Datenpunkte, C — States.
/// Geteilt von WinForms- und Avalonia-Oberfläche.
///
/// Die Warntexte stehen bewusst hier und nicht in den Oberflächen: Sie sind Teil der
/// fachlichen Aussage („Prüfliste, keine Löschliste") und müssen in beiden Fassungen
/// wortgleich erscheinen.
/// </summary>
public static class OrphansPresenter
{
    // ------------------------------------------------------------------ Warnhinweise

    public const string WarningA =
        "Prüfliste, keine Löschliste. Hier stehen Objekte, deren Adapter-Instanz im Backup fehlt. " +
        "Das kann auch an einer nur vorübergehend deinstallierten Instanz liegen.";

    public const string WarningB =
        "Kandidatenliste, keine Löschliste. Nutzung in externen Systemen oder per zusammengesetzten IDs " +
        "ist hier nicht vollständig erkennbar. Vor dem Löschen im laufenden System manuell prüfen.\n" +
        "Steht in der Bewertung „Expertenmodus\", ist der Datenpunkt im Admin nur zu sehen, wenn dort " +
        "der Experten-Modus eingeschaltet ist — sonst sucht man ihn dort vergeblich.";

    public const string WarningC =
        "Auswertung der states.jsonl — der einzigen Stelle im Backup, die etwas über die tatsächliche " +
        "Nutzung aussagt. Auch hier gilt: Prüfliste, keine Löschliste. Aliasse sind ausgenommen: Sie " +
        "haben keinen eigenen Wert, gelesen und geschrieben wird ihr Ziel.";

    // ------------------------------------------------------------------ Analyse A

    public static readonly string[] ColumnsA =
        { "Fehlende Instanz", "Objekt-ID", "Typ", "Name" };

    public static List<OrphanObject> FilterA(IEnumerable<OrphanObject> all, string? term)
    {
        var t = (term ?? "").Trim();
        return t.Length == 0
            ? all.ToList()
            : all.Where(o => o.Id.Contains(t, StringComparison.OrdinalIgnoreCase)
                          || o.MissingInstance.Contains(t, StringComparison.OrdinalIgnoreCase))
                 .ToList();
    }

    public static string CountA(int shown, IReadOnlyList<OrphanObject> all)
    {
        if (all.Count == 0)
            return "Keine Objekt-Leichen gefunden — jedes Objekt gehört zu einer vorhandenen Instanz.";

        var groups = all.Select(o => o.MissingInstance).Distinct().Count();
        return shown == all.Count
            ? $"{all.Count:N0} Objekte aus {groups} fehlenden Instanzen"
            : $"{shown:N0} von {all.Count:N0} Objekten";
    }

    public static string[] RowA(OrphanObject o) => new[] { o.MissingInstance, o.Id, o.TypeText, o.Name };

    // ------------------------------------------------------------------ Analyse B

    public static readonly string[] ColumnsB =
        { "Datenpunkt-ID", "Name", "In Skripten", "In VIS", "Alias-Ziel", "Logging",
          "In Chart", "Zuletzt geändert", "Bewertung", ValueColumn };

    /// <summary>Wie <see cref="ColumnsB"/>, zusätzlich mit dem Alter in Tagen als Zahl.</summary>
    public static readonly string[] CsvColumnsB =
        { "Datenpunkt-ID", "Name", "In Skripten", "In VIS", "Alias-Ziel", "Logging",
          "In Chart", "Zuletzt geändert", "Alter (Tage)", "Bewertung", ValueColumn };

    /// <summary>
    /// <paramref name="showAll"/> = false zeigt nur die Kandidaten; true auch die
    /// Datenpunkte, die eine der Prüfungen bestanden haben — zum Nachvollziehen, warum.
    /// </summary>
    public static List<UnusedDatapoint> FilterB(IEnumerable<UnusedDatapoint> all, bool showAll, string? term)
    {
        var q = showAll ? all : all.Where(u => u.IsCandidate);

        var t = (term ?? "").Trim();
        if (t.Length > 0) q = q.Where(u => u.Id.Contains(t, StringComparison.OrdinalIgnoreCase));

        return q.ToList();
    }

    public static string CountB(IReadOnlyList<UnusedDatapoint> all, bool hasStates)
    {
        var candidates = all.Count(u => u.IsCandidate);
        var strong = all.Count(u => u.IsStrongCandidate);
        var active = all.Count(u => u.IsCandidate && u.RecentlyChanged);

        return hasStates
            ? $"{candidates} Kandidaten von {all.Count} · {strong} davon tot, {active} noch aktiv"
            : $"{candidates} Kandidaten von {all.Count} geprüften";
    }

    /// <summary>
    /// Drei Stufen statt zwei: Ein Kandidat, dessen Wert sich noch letzte Woche geändert
    /// hat, wird von irgendetwas beschrieben — den darf man nicht mit einem seit Jahren
    /// toten Datenpunkt in denselben Topf werfen.
    /// </summary>
    public static RowEmphasis EmphasisB(UnusedDatapoint u) =>
        !u.IsCandidate ? RowEmphasis.Muted
        : u.RecentlyChanged ? RowEmphasis.Warn
        : RowEmphasis.Problem;

    /// <summary>Siehe <see cref="DisplayRowC"/> — gekürzter Wert für die Anzeige.</summary>
    public static string[] DisplayRowB(UnusedDatapoint u) =>
        new[] { u.Id, u.Name, u.InScriptsText, u.InVisText, u.AliasText, u.LoggingText,
                u.InChartText, u.LastChangeText, u.Verdict, u.ValText };

    /// <summary>Siehe <see cref="RowC"/> — vollständiger Wert für die CSV.</summary>
    public static string[] RowB(UnusedDatapoint u) =>
        new[] { u.Id, u.Name, u.InScriptsText, u.InVisText, u.AliasText, u.LoggingText,
                u.InChartText,
                u.LastChange?.ToString("dd.MM.yyyy HH:mm") ?? (u.HasState ? "" : "nie beschrieben"),
                u.AgeDays?.ToString() ?? "", u.Verdict, u.Val };

    /// <summary>
    /// Beschriftung des Schalters „alle anzeigen". Fehlen VIS-Views im Backup, fällt eine
    /// der Prüfungen aus — das muss sichtbar sein statt still das Ergebnis zu verfälschen.
    /// </summary>
    public static string ShowAllLabelB(bool hasVisViews) =>
        hasVisViews ? "Alle geprüften Datenpunkte anzeigen"
                    : "Alle anzeigen (Achtung: keine VIS-Views im Backup)";

    // ------------------------------------------------------------------ Analyse C

    public static readonly string[] ColumnsC =
        { "Datenpunkt-ID", "Name", "Zuletzt geändert", "Schreibbar", "Quelle", "Qualität",
          "Quittiert", ValueColumn };

    /// <summary>Wie <see cref="ColumnsC"/>, zusätzlich mit dem Alter in Tagen als Zahl.</summary>
    public static readonly string[] CsvColumnsC =
        { "Datenpunkt-ID", "Name", "Zuletzt geändert", "Alter (Tage)", "Schreibbar", "Quelle",
          "Qualität", "Quittiert", ValueColumn };

    /// <summary>
    /// Beschriftung der Wertspalte. Sie steht in allen Listen am Ende — die gewohnte
    /// Spaltenfolge bleibt damit unverändert, und beim Scrollen verschwindet als Erstes das,
    /// was am seltensten gebraucht wird.
    /// </summary>
    public const string ValueColumn = "Letzter Wert";

    public static readonly string[] ViewLabelsC =
        { "States ohne Objekt (Werte-Leichen)",
          // Nicht „nie beschrieben": Geprüft wird allein, ob es in states.jsonl einen
          // Eintrag gibt. Ein Datenpunkt kann im laufenden System längst Werte geliefert
          // haben und hier trotzdem stehen — etwa wenn sein State gelöscht wurde oder die
          // States-Datenbank im Backup unvollständig ist. Die alte Beschriftung behauptete
          // mehr, als die Prüfung hergibt (Rückfrage aus der Praxis).
          // Aliasse sind hier ausgenommen: Sie haben nie einen eigenen Wert, weil der
          // js-controller auf das Ziel durchreicht (zweite Rückfrage aus der Praxis).
          "Objekte ohne Wert (kein Eintrag in der States-DB, ohne Aliasse)",
          "Älteste Datenpunkte (nach letzter Änderung)",
          "Auffällige Qualität (Code ungleich gut)",
          "Nicht quittierte Befehle (ack = nein)" };

    /// <summary>
    /// Obergrenze der angezeigten Zeilen. Die Sicht „Älteste" umfasst alle States;
    /// vollständig dargestellt wäre die Liste unbenutzbar lang. Die ersten 2.000 reichen
    /// für die Frage, was eingeschlafen ist — der CSV-Export enthält immer alles.
    /// </summary>
    public const int DisplayLimitOldest = 2000;

    public static List<StateRow> RowsC(StateReport? states, StateView view) => states is null
        ? new List<StateRow>()
        : view switch
        {
            StateView.WithoutObject => states.StatesWithoutObject,
            StateView.WithoutState => states.ObjectsWithoutState,
            StateView.Oldest => states.All,
            StateView.BadQuality => states.BadQuality,
            _ => states.Unacknowledged
        };

    public static List<StateRow> FilterC(IEnumerable<StateRow> rows, string? term)
    {
        var t = (term ?? "").Trim();
        return t.Length == 0
            ? rows.ToList()
            : rows.Where(r => r.Id.Contains(t, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>Wie viele Zeilen die Anzeige höchstens zeigt — nur „Älteste" wird begrenzt.</summary>
    public static int LimitC(StateView view, int rowCount) =>
        view == StateView.Oldest ? Math.Min(DisplayLimitOldest, rowCount) : rowCount;

    public static string CountC(int shown, int total, bool filtered, int limit)
    {
        var suffix = total > limit ? $" (angezeigt: {limit:N0})" : "";
        return filtered
            ? $"{shown:N0} von {total:N0} Einträgen{suffix}"
            : $"{shown:N0} Einträge{suffix}";
    }

    /// <summary>
    /// Zwei Zeilen Kennzahlen samt Altersverteilung — sie ordnet jede Sicht ein.
    /// Zeilentrenner ist <c>\n</c>.
    /// </summary>
    public static string StatsC(StateReport states)
    {
        var ages = string.Join("   ", states.Ages.Where(b => b.Count > 0)
                                                 .Select(b => $"{b.Label}: {b.Count:N0}"));
        // Die ausgenommenen Aliasse werden mitgezählt angezeigt, sonst wirkt die Differenz
        // zwischen state-Objekten und den beiden Listen wie ein Zählfehler.
        var alias = states.AliasesWithoutOwnState > 0
            ? $" · {states.AliasesWithoutOwnState:N0} Aliasse (kein eigener Wert)"
            : "";

        return $"{states.TotalStates:N0} States · {states.TotalStateObjects:N0} state-Objekte · " +
               $"{states.StatesWithoutObject.Count:N0} ohne Objekt · " +
               $"{states.ObjectsWithoutState.Count:N0} ohne Wert{alias}\n" +
               "Letzte Wertänderung —   " + ages;
    }

    /// <summary>
    /// Wie stark eine Zeile der Sicht C hervorgehoben wird.
    ///
    /// Ersatz- und Startwerte (Qualität ungleich gut, aber ohne Fehlerbit) sind kein Befund:
    /// Der Wert stammt nur nicht vom eigentlichen Erzeuger. Sie bleiben gedämpft — auch wenn
    /// sie alt sind, denn ein Wert, den nie ein Gerät geschrieben hat, ist zwangsläufig alt.
    /// Sonst stünde in der Sicht „Auffällige Qualität" fast jede Zeile farbig und die wenigen
    /// echten Störungen gingen darin unter — Startwerte stellen in der Praxis die große Mehrheit.
    /// </summary>
    public static RowEmphasis EmphasisC(StateRow r)
    {
        if (r.Quality != 0 && !r.QualityIsFault && r.HasObject && r.HasState && r.Ack)
            return RowEmphasis.Muted;

        if (!r.Ack || r.QualityIsFault) return RowEmphasis.Warn;
        if (!r.HasObject || !r.HasState) return RowEmphasis.Problem;
        return r.AgeDays > 365 ? RowEmphasis.Problem : RowEmphasis.None;
    }


    /// <summary>
    /// Die Anzeigezeile. Der Wert steht hier in der gekürzten Fassung — eine Tabellenzelle
    /// verträgt nichts anderes (siehe <see cref="StateInfo.FormatVal"/>).
    /// </summary>
    public static string[] DisplayRowC(StateRow r) =>
        new[] { r.Id, r.Name, r.HasState ? r.LastChangeText : "nie beschrieben",
                r.WritableText, r.From, r.QualityText, r.HasState ? r.AckText : "—",
                r.ValText };

    /// <summary>
    /// Die CSV-Zeile. Anders als in der Anzeige steht hier der <b>vollständige</b>
    /// gespeicherte Wert: Eine CSV wird weiterverarbeitet und nicht überflogen, und die
    /// Maskierung verkraftet auch mehrzeilige Werte.
    /// </summary>
    public static string[] RowC(StateRow r) =>
        new[] { r.Id, r.Name,
                r.LastChange?.ToString("dd.MM.yyyy HH:mm") ?? (r.HasState ? "" : "nie beschrieben"),
                r.AgeDays?.ToString() ?? "", r.WritableText, r.From, r.QualityText,
                r.HasState ? r.AckText : "", r.Val };

    /// <summary>Vorgeschlagener Dateiname des CSV-Exports, passend zur gewählten Sicht.</summary>
    public static string CsvNameC(StateView view) => view switch
    {
        StateView.WithoutObject => "states-ohne-objekt.csv",
        StateView.WithoutState => "objekte-ohne-wert.csv",
        StateView.Oldest => "states-nach-alter.csv",
        StateView.BadQuality => "states-mit-stoerung.csv",
        _ => "states-nicht-quittiert.csv"
    };

    /// <summary>Beschriftung des dritten Untertabs — mit Hinweis, wenn states.jsonl fehlt.</summary>
    public static string TabTitleC(bool hasStates) =>
        hasStates ? "C — States" : "C — States (nicht im Backup)";

    /// <summary>
    /// Die Werte-Leichen nach Namensraum gruppiert — Grundlage des Aufräum-Skripts.
    /// Unabhängig von der gerade gewählten Sicht.
    /// </summary>
    public static List<(string Namespace, IReadOnlyList<string> Ids)> CleanupGroups(StateReport? states) =>
        states is null
            ? new List<(string, IReadOnlyList<string>)>()
            : states.StatesWithoutObject
                    .GroupBy(r => r.Namespace, StringComparer.Ordinal)
                    .Select(g => (Namespace: g.Key, Ids: (IReadOnlyList<string>)g.Select(r => r.Id).ToList()))
                    .ToList();
}

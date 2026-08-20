namespace IobBackupAnalyzer.Core;

/// <summary>Häkchenzustand eines Namensraums im Aufräum-Dialog.</summary>
public enum GroupCheck
{
    /// <summary>Keine der angezeigten IDs ist ausgewählt.</summary>
    None,
    /// <summary>Ein Teil der angezeigten IDs ist ausgewählt.</summary>
    Partial,
    /// <summary>Alle angezeigten IDs sind ausgewählt.</summary>
    All
}

/// <summary>
/// Die Auswahl für das Aufräum-Skript: Namensräume mit ihren Waisen-States, dazu welche
/// davon angehakt sind. UI-neutral, damit WinForms- und Avalonia-Fassung dieselbe Bedienung
/// bekommen — nur das Zeichnen des Baums unterscheidet sich.
///
/// <b>Warum einzelne IDs und nicht nur ganze Namensräume?</b> Ein Namensraum ist selten
/// durchgehend Müll: Von einem Adapter, der nur teilweise entfernt wurde, bleiben Werte
/// stehen, die man behalten will (etwa eine noch gebrauchte Historie), neben solchen, die
/// weg sollen. Wer nur gruppenweise wählen kann, muss dann entweder zu viel löschen oder
/// gar nichts.
///
/// <b>Was der Filter tut:</b> Er blendet IDs aus, er wählt sie nicht ab. „Alle"/„Keine" und
/// das Häkchen am Namensraum wirken deshalb ausdrücklich nur auf das gerade Sichtbare —
/// alles andere bleibt, wie es war. Damit eine so entstandene Auswahl außerhalb der Suche
/// nicht unbemerkt bleibt, nennt <see cref="CountText"/> immer die Gesamtzahl.
/// </summary>
public sealed class CleanupSelection
{
    /// <summary>Ein Namensraum samt seiner Waisen-States.</summary>
    public sealed class Group
    {
        /// <summary>Namensraum, z. B. <c>alexa2.0</c> — die ersten beiden ID-Segmente.</summary>
        public required string Namespace { get; init; }

        /// <summary>Alle IDs des Namensraums, ordinal sortiert.</summary>
        public required IReadOnlyList<string> Ids { get; init; }

        /// <summary>
        /// Die IDs, die der aktuelle Filter durchlässt — ohne Filter identisch mit
        /// <see cref="Ids"/>.
        /// </summary>
        public IReadOnlyList<string> Visible { get; internal set; } = Array.Empty<string>();
    }

    private readonly List<Group> _groups;
    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);
    private List<Group> _visible;
    private string _filter = "";

    public CleanupSelection(IEnumerable<(string Namespace, IReadOnlyList<string> Ids)> groups)
    {
        _groups = (groups ?? Enumerable.Empty<(string, IReadOnlyList<string>)>())
            .Select(g => new Group
            {
                Namespace = g.Namespace,
                Ids = (g.Ids ?? Array.Empty<string>())
                      .Where(id => !string.IsNullOrWhiteSpace(id))
                      .Distinct(StringComparer.Ordinal)
                      .OrderBy(id => id, StringComparer.Ordinal)
                      .ToList()
            })
            .Where(g => g.Ids.Count > 0)
            // Größte Namensräume zuerst — dort lohnt das Aufräumen am meisten. Die
            // Sortierung steht hier und nicht in den Oberflächen, damit beide Fassungen
            // dieselbe Reihenfolge zeigen.
            .OrderByDescending(g => g.Ids.Count)
            .ThenBy(g => g.Namespace, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var g in _groups) g.Visible = g.Ids;
        _visible = _groups;
        TotalIds = _groups.Sum(g => g.Ids.Count);
    }

    /// <summary>Zahl aller Waisen-States über alle Namensräume — unabhängig vom Filter.</summary>
    public int TotalIds { get; }

    /// <summary>Der aktuelle Suchbegriff; leer, wenn nicht gefiltert wird.</summary>
    public string Filter => _filter;

    /// <summary>Die Namensräume, in denen der Filter mindestens eine ID durchlässt.</summary>
    public IReadOnlyList<Group> VisibleGroups => _visible;

    /// <summary>Zahl der gerade sichtbaren IDs über alle Namensräume.</summary>
    public int VisibleIdCount => _visible.Sum(g => g.Visible.Count);

    public int SelectedCount => _selected.Count;

    /// <summary>
    /// Setzt den Suchbegriff. Verglichen wird gegen die vollständige ID ohne Rücksicht auf
    /// Groß- und Kleinschreibung; weil jede ID mit ihrem Namensraum beginnt, findet die
    /// Suche nach einem Adapternamen automatisch dessen ganze Gruppe.
    /// </summary>
    public void SetFilter(string? term)
    {
        _filter = (term ?? "").Trim();

        if (_filter.Length == 0)
        {
            foreach (var g in _groups) g.Visible = g.Ids;
            _visible = _groups;
            return;
        }

        var hits = new List<Group>();
        foreach (var g in _groups)
        {
            g.Visible = g.Ids.Where(id => id.Contains(_filter, StringComparison.OrdinalIgnoreCase)).ToList();
            if (g.Visible.Count > 0) hits.Add(g);
        }
        _visible = hits;
    }

    public bool IsSelected(string id) => _selected.Contains(id);

    /// <summary>Wie viele IDs des Namensraums insgesamt ausgewählt sind — auch ausgeblendete.</summary>
    public int SelectedIn(Group group) => group.Ids.Count(_selected.Contains);

    public void Select(string id, bool on)
    {
        if (on) _selected.Add(id);
        else _selected.Remove(id);
    }

    /// <summary>Setzt das Häkchen für alle sichtbaren IDs des Namensraums.</summary>
    public void SelectGroup(Group group, bool on)
    {
        foreach (var id in group.Visible) Select(id, on);
    }

    /// <summary>Setzt das Häkchen für alles, was der Filter gerade durchlässt.</summary>
    public void SelectAllVisible(bool on)
    {
        foreach (var g in _visible) SelectGroup(g, on);
    }

    /// <summary>
    /// Zustand des Gruppenhäkchens, bezogen auf die sichtbaren IDs — sonst zeigte das
    /// Häkchen einen Zustand an, den ein Klick darauf gar nicht herstellen kann.
    /// </summary>
    public GroupCheck StateOf(Group group)
    {
        if (group.Visible.Count == 0) return GroupCheck.None;

        var n = group.Visible.Count(_selected.Contains);
        return n == 0 ? GroupCheck.None
             : n == group.Visible.Count ? GroupCheck.All
             : GroupCheck.Partial;
    }

    /// <summary>
    /// Was ins Skript kommt: alle angehakten IDs, ordinal sortiert — ausdrücklich auch die,
    /// die der Filter gerade ausblendet.
    /// </summary>
    public IReadOnlyList<string> SelectedIds =>
        _selected.OrderBy(id => id, StringComparer.Ordinal).ToList();

    /// <summary>
    /// Beschriftung eines Namensraum-Knotens: Anzahl, bei aktivem Filter zusätzlich wie
    /// viele davon gerade sichtbar sind, und wie viele angehakt wurden. Letzteres steht
    /// dort, weil ein zugeklappter Knoten seine Auswahl sonst nicht zeigt.
    /// </summary>
    public string GroupLabel(Group group)
    {
        var basis = group.Visible.Count == group.Ids.Count
            ? $"{group.Ids.Count:N0}"
            : $"{group.Visible.Count:N0} von {group.Ids.Count:N0}";

        var selected = SelectedIn(group);
        return selected == 0
            ? $"{group.Namespace}   ({basis})"
            : $"{group.Namespace}   ({basis}, ausgewählt: {selected:N0})";
    }

    /// <summary>Zeile unter dem Baum — nennt immer die Gesamtauswahl, nicht nur die sichtbare.</summary>
    public string CountText
    {
        get
        {
            var text = $"Ausgewählt: {SelectedCount:N0} von {TotalIds:N0} Werten";
            return _filter.Length == 0 ? text : text + $" · Suche zeigt {VisibleIdCount:N0}";
        }
    }

    /// <summary>
    /// Hinweis, wenn die Suche einen Teil der Auswahl verdeckt. Sonst wundert man sich über
    /// ein Skript, das mehr enthält als der Baum gerade zeigt.
    /// </summary>
    public string? HiddenSelectionHint
    {
        get
        {
            if (_filter.Length == 0 || SelectedCount == 0) return null;

            var visibleSelected = _visible.Sum(g => g.Visible.Count(_selected.Contains));
            var hidden = SelectedCount - visibleSelected;
            return hidden <= 0
                ? null
                : $"{hidden:N0} ausgewählte Werte blendet die Suche gerade aus — im Skript stehen sie trotzdem.";
        }
    }
}

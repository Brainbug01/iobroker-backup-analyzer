using IobBackupAnalyzer.Core;

namespace IobBackupAnalyzer.App;

/// <summary>
/// Rüstet eine ListView zum Sortieren nach: Klick auf einen Spaltenkopf sortiert, ein
/// zweiter Klick dreht die Richtung um. Ein Pfeil im Spaltenkopf zeigt an, wonach gerade
/// sortiert ist. Zusätzlich lassen sich die Spalten per Maus umordnen.
///
/// Sortiert wird einmalig durch Umbauen der Zeilen — nicht über einen dauerhaften
/// <see cref="ListView.ListViewItemSorter"/>. Der würde bei jedem Hinzufügen einer Zeile
/// erneut sortieren, was beim Füllen großer Listen (im Vergleich sind fünfstellige
/// Zeilenzahlen möglich) unnötig teuer wäre.
/// </summary>
internal static class ListViewSort
{
    private const string Ascending = "  ▲";
    private const string Descending = "  ▼";

    /// <summary>
    /// Hängt Sortierung und Spaltenverschiebung an. Für Listen, die ihre Reihenfolge selbst
    /// verwalten, gibt es stattdessen <see cref="ShowMarker"/> und <see cref="EnableReorder"/>.
    /// </summary>
    public static void Attach(ListView list)
    {
        EnableReorder(list);

        var sortColumn = -1;
        var ascending = true;

        list.ColumnClick += (_, e) =>
        {
            if (e.Column == sortColumn) ascending = !ascending;
            else { sortColumn = e.Column; ascending = true; }

            Apply(list, sortColumn, ascending);
            ShowMarker(list, sortColumn, ascending);
        };
    }

    /// <summary>
    /// Erlaubt das Verschieben der Spalten per Maus. Sinnvoll auch für Listen mit eigener
    /// Sortierlogik — die Reihenfolge der Spalten ist davon unabhängig.
    /// </summary>
    public static void EnableReorder(ListView list) => list.AllowColumnReorder = true;

    /// <summary>
    /// Setzt den Sortierpfeil auf die angegebene Spalte und entfernt ihn von allen anderen.
    /// Getrennt von <see cref="Attach"/>, damit Listen mit eigener Sortierung denselben
    /// Hinweis anzeigen können.
    /// </summary>
    public static void ShowMarker(ListView list, int column, bool ascending)
    {
        for (var i = 0; i < list.Columns.Count; i++)
        {
            var text = StripMarker(list.Columns[i].Text);
            list.Columns[i].Text = i == column ? text + (ascending ? Ascending : Descending) : text;
        }
    }

    /// <summary>Spaltentitel ohne Sortierpfeil — für alles, was den Titel weiterverwendet.</summary>
    public static string StripMarker(string columnText) =>
        columnText.EndsWith(Ascending, StringComparison.Ordinal)
        || columnText.EndsWith(Descending, StringComparison.Ordinal)
            ? columnText[..^Ascending.Length]
            : columnText;

    private static void Apply(ListView list, int column, bool ascending)
    {
        if (list.Items.Count < 2) return;

        var items = list.Items.Cast<ListViewItem>().ToList();

        // OrderBy ist stabil: Zeilen mit gleichem Wert behalten ihre bisherige Reihenfolge.
        // Damit bleibt die vom Analyse-Code gewählte Zweitsortierung erhalten.
        var comparer = new CellComparer(column);
        var sorted = ascending
            ? items.OrderBy(i => i, comparer).ToArray()
            : items.OrderByDescending(i => i, comparer).ToArray();

        var selected = list.SelectedItems.Cast<ListViewItem>().ToHashSet();

        list.BeginUpdate();
        list.Items.Clear();
        list.Items.AddRange(sorted);
        list.EndUpdate();

        // Die Auswahl gehört zur Zeile, nicht zu ihrer Position — sie muss den Umbau überleben.
        foreach (var item in sorted) item.Selected = selected.Contains(item);
        if (list.SelectedItems.Count > 0) list.SelectedItems[0].EnsureVisible();
    }

    /// <summary>
    /// Vergleicht zwei Zeilen anhand einer Spalte. Wie verglichen wird — numerisch, nach
    /// Version, chronologisch oder alphabetisch —, entscheidet <see cref="DisplayCompare"/>
    /// anhand des angezeigten Texts.
    /// </summary>
    private sealed class CellComparer : IComparer<ListViewItem>
    {
        private readonly int _column;

        public CellComparer(int column) => _column = column;

        public int Compare(ListViewItem? x, ListViewItem? y) =>
            DisplayCompare.Compare(Cell(x), Cell(y));

        private string Cell(ListViewItem? item) =>
            item is not null && _column >= 0 && _column < item.SubItems.Count
                ? item.SubItems[_column].Text
                : "";
    }
}

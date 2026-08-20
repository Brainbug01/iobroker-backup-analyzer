using IobBackupAnalyzer.Core;

namespace IobBackupAnalyzer.App;

/// <summary>
/// Auflistung der in VIS verwendeten Datenpunkte, getrennt nach VIS 1 (vis.0) und
/// VIS 2 (vis-2.0).
/// </summary>
public sealed class VisTab : UserControl
{
    private readonly Label _summary = new();
    private readonly TextBox _filter = new();
    private readonly ComboBox _scope = new();
    private readonly Label _count = new();
    private readonly Button _csv = new();
    private readonly ListView _list = new();
    private readonly ListView _usages = new();
    private readonly Label _usageHeader = new();
    private readonly Label _placeholder = new();

    private BackupData? _data;
    private List<VisDatapoint> _all = new();
    private List<VisDatapoint> _filtered = new();

    private int _sortColumn = -1;
    private bool _sortAscending = true;

    public VisTab()
    {
        BuildUi();
    }

    private void BuildUi()
    {
        Padding = new Padding(8);

        var head = TabLayout.TopBar(86);

        _summary.Location = new Point(0, 0);
        _summary.Size = new Size(1100, 40);
        _summary.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        var lbl = new Label { Text = "Filter:", Location = new Point(0, 51), Size = new Size(44, 20) };
        _filter.Location = new Point(46, 48);
        _filter.Size = new Size(280, 24);
        _filter.PlaceholderText = "Datenpunkt-ID oder View …";
        _filter.TextChanged += (_, _) => ApplyFilter();

        _scope.Location = new Point(336, 48);
        _scope.Size = new Size(230, 24);
        _scope.DropDownStyle = ComboBoxStyle.DropDownList;
        // Reihenfolge und Beschriftung kommen aus dem Presenter, damit die Auswahl in
        // beiden Oberflächen dieselbe ist — der Index wird direkt auf VisScope gecastet.
        _scope.Items.AddRange(VisPresenter.ScopeLabels.Cast<object>().ToArray());
        _scope.SelectedIndex = 0;
        _scope.SelectedIndexChanged += (_, _) => ApplyFilter();

        _count.Location = new Point(580, 51);
        _count.Size = new Size(340, 20);
        _count.ForeColor = SystemColors.GrayText;

        _csv.Text = "Als CSV exportieren";
        _csv.Size = new Size(160, 26);
        _csv.Location = TabLayout.RightAligned(160, 47);
        _csv.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _csv.Click += (_, _) => ExportCsv();

        head.Controls.AddRange(new Control[] { _summary, lbl, _filter, _scope, _count, _csv });

        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.GridLines = true;
        _list.HideSelection = false;
        _list.Columns.Add("Datenpunkt-ID", 360);
        _list.Columns.Add("Name", 170);
        _list.Columns.Add("VIS 1", 55);
        _list.Columns.Add("VIS 2", 55);
        _list.Columns.Add("Widgets", 70, HorizontalAlignment.Right);
        _list.Columns.Add("Vorhanden", 85);
        // Bei Aliassen ist die ID allein nichtssagend — erst das Ziel zeigt das echte Gerät.
        _list.Columns.Add("Alias → Ziel", 280);
        _list.Columns.Add("Widget-Typen", 190);
        _list.Columns.Add("Views", 240);
        _list.MultiSelect = true;
        _list.ColumnClick += OnColumnClick;
        _list.SelectedIndexChanged += (_, _) => ShowUsages();

        // Unterer Bereich: alle Fundstellen des gewählten Datenpunkts, Widget für Widget.
        _usageHeader.Dock = DockStyle.Top;
        _usageHeader.Height = 22;
        _usageHeader.Padding = new Padding(2, 3, 2, 0);
        _usageHeader.Font = new Font(Font, FontStyle.Bold);
        _usageHeader.Text = "Fundstellen";

        _usages.Dock = DockStyle.Fill;
        _usages.View = View.Details;
        _usages.FullRowSelect = true;
        _usages.GridLines = true;
        _usages.Columns.Add("VIS", 60);
        // Eine Installation kann mehrere VIS-Projekte haben — beobachtet sind schon drei.
        // Ohne diese Spalte wäre nicht erkennbar, in welchem eine View liegt.
        _usages.Columns.Add("Projekt", 120);
        _usages.Columns.Add("View", 220);
        _usages.Columns.Add("Widget", 150);
        _usages.Columns.Add("Widget-Typ", 220);
        _usages.Columns.Add("Widget-Set", 140);
        _usages.Columns.Add("Feld", 200);
        // Zeigt, ob das Widget den Wert liest oder ein Zustandsattribut wie den Zeitstempel.
        _usages.Columns.Add("Zugriff", 70);
        _usages.MultiSelect = true;
        _usages.HideSelection = false;

        // Widget-IDs und Datenpunkt-IDs müssen kopierbar sein, nicht abtippbar.
        ListViewCopy.Attach(_list);
        ListViewCopy.Attach(_usages);

        // Die obere Liste sortiert auf der Datenliste (ApplySort), damit die Reihenfolge
        // einen Filterwechsel überlebt; die Fundstellenliste unten sortiert für sich.
        ListViewSort.EnableReorder(_list);
        ListViewSort.Attach(_usages);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            Panel1MinSize = 120,
            Panel2MinSize = 100
        };

        // SplitterDistance erst setzen, wenn die echte Höhe feststeht (sonst wirft WinForms).
        var splitInit = false;
        split.SizeChanged += (_, _) =>
        {
            if (splitInit || split.Height < 300) return;
            split.SplitterDistance = split.Height * 3 / 5;
            splitInit = true;
        };

        split.Panel1.Controls.Add(_list);
        split.Panel2.Controls.Add(_usages);
        split.Panel2.Controls.Add(_usageHeader);

        _placeholder.Dock = DockStyle.Fill;
        _placeholder.TextAlign = ContentAlignment.MiddleCenter;
        _placeholder.ForeColor = SystemColors.GrayText;
        _placeholder.Text = "Kein Backup geladen.";

        Controls.Add(split);
        Controls.Add(_placeholder);
        Controls.Add(head);
    }

    public void SetAvailable(bool available)
    {
        _placeholder.Visible = !available;
        if (!available)
        {
            _placeholder.Text = _data is null
                ? "Kein Backup geladen.\r\n\r\nBitte oben eine Datei öffnen oder hineinziehen."
                : "Für die VIS-Auswertung wird ein Voll-Backup benötigt.\r\n\r\n" +
                  "Die geladene Datei enthält nur Skripte.";
            _placeholder.BringToFront();
        }
    }

    public void SetData(BackupData data)
    {
        _data = data;
        _sortColumn = -1;

        if (data.Kind != BackupKind.Full)
        {
            _all = new List<VisDatapoint>();
            _list.Items.Clear();
            _summary.Text = "";
            return;
        }

        Cursor = Cursors.WaitCursor;
        try
        {
            _all = VisAnalyzer.Analyze(data);
        }
        finally
        {
            Cursor = Cursors.Default;
        }

        _summary.Text = VisPresenter.SummaryText(_all, data).Replace("\n", "\r\n");

        if (data.VisViews.Count == 0)
        {
            _list.Items.Clear();
            return;
        }

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (_data is null) return;

        _filtered = VisPresenter.Filter(_all, (VisScope)_scope.SelectedIndex, _filter.Text);
        ApplySort();
        FillList();

        _count.Text = VisPresenter.CountText(_filtered.Count, _all.Count);
    }

    private void FillList()
    {
        _list.BeginUpdate();
        _list.Items.Clear();

        foreach (var d in _filtered)
        {
            var item = new ListViewItem(VisPresenter.DisplayRow(d)) { Tag = d };

            // Fehlender Datenpunkt = totes Widget, das hervorgehoben gehört.
            // Ein Alias, dessen Ziel fehlt, ist genauso kaputt — nur eine Ebene tiefer.
            if (!d.ExistsInBackup || d.AliasTargetMissing) item.ForeColor = Color.Firebrick;

            _list.Items.Add(item);
        }

        _list.EndUpdate();
        ShowUsages();
    }

    /// <summary>Listet jede einzelne Fundstelle des gewählten Datenpunkts auf.</summary>
    private void ShowUsages()
    {
        _usages.BeginUpdate();
        _usages.Items.Clear();

        if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not VisDatapoint d)
        {
            _usageHeader.Text = VisPresenter.UsageHeader(null, "");
            _usages.EndUpdate();
            return;
        }

        _usageHeader.Text = VisPresenter.UsageHeader(d, "Rechtsklick oder Strg+C zum Kopieren");

        foreach (var u in VisPresenter.SortedUsages(d))
        {
            var item = new ListViewItem(VisPresenter.UsageRow(u));

            if (!d.ExistsInBackup) item.ForeColor = Color.Firebrick;
            _usages.Items.Add(item);
        }

        _usages.EndUpdate();
    }

    private void OnColumnClick(object? sender, ColumnClickEventArgs e)
    {
        if (e.Column == _sortColumn) _sortAscending = !_sortAscending;
        else { _sortColumn = e.Column; _sortAscending = true; }

        ListViewSort.ShowMarker(_list, _sortColumn, _sortAscending);
        ApplySort();
        FillList();
    }

    private void ApplySort() =>
        _filtered = VisPresenter.Sort(_filtered, _sortColumn, _sortAscending);

    /// <summary>
    /// Eine Zeile je Fundstelle statt je Datenpunkt — so ist in einer Tabellenkalkulation
    /// direkt filterbar, welches Widget in welcher View welchen Datenpunkt wofür nutzt.
    /// </summary>
    private void ExportCsv() =>
        CsvExport.Save(this, "vis-datenpunkte.csv",
            VisPresenter.CsvColumns,
            VisPresenter.CsvRows(_filtered));
}

using IobBackupAnalyzer.Core;

namespace IobBackupAnalyzer.App;

/// <summary>Säule 1: Backup-Inventar.</summary>
public sealed class OverviewTab : UserControl
{
    private readonly Label _header = new();
    private readonly Label _metrics = new();

    // Warnzeile über dem Filter: Instanzen, die mehr Objekte haben als ihr Limit erlaubt.
    // Sie erscheint nur, wenn es etwas zu melden gibt — und schiebt dann die Filterzeile
    // nach unten (siehe PositionHead).
    private readonly Panel _head = TabLayout.TopBar(HeadHeight);
    private readonly Label _limitWarning = new();
    private readonly Label _lblFilter = new();
    private readonly ToolTip _tips = new() { AutoPopDelay = 20000 };

    private readonly TextBox _filter = new();
    private readonly Label _count = new();
    private readonly Button _csv = new();
    private readonly ListView _list = new();
    private readonly Label _placeholder = new();

    // Unterer Bereich: installierte Adapter ohne eigene Instanz.
    private readonly ListView _noInstance = new();
    private readonly Label _noInstanceHeader = new();
    private readonly Label _noInstanceHint = new();
    private readonly Button _noInstanceCsv = new();

    private BackupData? _data;
    private List<AdapterInstance> _filtered = new();
    private List<AdapterWithoutInstance> _adaptersWithoutInstance = new();

    private int _sortColumn = -1;
    private bool _sortAscending = true;

    /// <summary>Höhe der Kopfleiste ohne Warnzeile.</summary>
    private const int HeadHeight = 104;

    /// <summary>Zusätzliche Höhe, sobald die Warnzeile sichtbar ist.</summary>
    private const int WarningHeight = 40;

    public OverviewTab()
    {
        BuildUi();
    }

    private void BuildUi()
    {
        Padding = new Padding(8);

        // ---------- Kopfbereich mit Kennzahlen ----------
        var head = _head;

        _header.Location = new Point(0, 0);
        _header.Size = new Size(1100, 24);
        _header.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
        _header.AutoEllipsis = true;
        _header.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        _metrics.Location = new Point(0, 28);
        _metrics.Size = new Size(1100, 46);
        _metrics.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        // Warnzeile über dem Filter, in gedecktem Gelb wie die übrigen Hinweiskästen der
        // App — auffällig genug, um gelesen zu werden, ohne wie ein Fehler auszusehen:
        // Das Objektlimit ist eine Leistungswarnung, kein Defekt.
        _limitWarning.BackColor = Color.FromArgb(255, 249, 219);
        _limitWarning.ForeColor = Color.FromArgb(90, 60, 0);
        _limitWarning.Size = new Size(TabLayout.DesignWidth, WarningHeight - 6);
        _limitWarning.Padding = new Padding(6, 3, 6, 3);
        _limitWarning.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _limitWarning.Visible = false;
        _tips.SetToolTip(_limitWarning, OverviewPresenter.ObjectLimitHint);

        _lblFilter.Text = "Filter:";
        _lblFilter.Size = new Size(44, 20);

        _filter.Size = new Size(320, 24);
        _filter.PlaceholderText = "Adaptername …";
        _filter.TextChanged += (_, _) => ApplyFilter();

        _count.Size = new Size(400, 20);
        _count.ForeColor = SystemColors.GrayText;

        _csv.Text = "Als CSV exportieren";
        _csv.Size = new Size(160, 26);
        _csv.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _csv.Click += (_, _) => ExportCsv();

        // Die senkrechten Positionen der Filterzeile setzt PositionHead — sie hängen
        // davon ab, ob die Warnzeile gerade sichtbar ist.
        PositionHead(false);

        head.Controls.AddRange(new Control[]
            { _header, _metrics, _limitWarning, _lblFilter, _filter, _count, _csv });

        // ---------- Instanztabelle ----------
        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.GridLines = true;
        _list.HideSelection = false;
        _list.Columns.Add("Adapter", 220);
        _list.Columns.Add("Instanz", 70, HorizontalAlignment.Right);
        _list.Columns.Add("Version", 110);
        _list.Columns.Add("Aktiviert", 90);
        _list.Columns.Add("Objekte", 100, HorizontalAlignment.Right);
        _list.MultiSelect = true;
        _list.ColumnClick += OnColumnClick;
        ListViewCopy.Attach(_list);
        // Sortiert wird auf der Datenliste (ApplySort), damit die Reihenfolge einen
        // Filterwechsel überlebt — von ListViewSort kommt nur die Spaltenverschiebung.
        ListViewSort.EnableReorder(_list);

        // ---------- Unterer Bereich: Adapter ohne eigene Instanz ----------
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 190, Padding = new Padding(0, 6, 0, 0) };

        _noInstanceHeader.Dock = DockStyle.Top;
        _noInstanceHeader.Height = 24;
        _noInstanceHeader.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        _noInstanceHeader.Text = "Installierte Adapter ohne eigene Instanz";
        _noInstanceHeader.TextAlign = ContentAlignment.MiddleLeft;

        // Ehrlicher Hinweis: instanzlos heißt nicht ungenutzt. Socket-Backends wie ws/
        // socketio und UI-Helfer laufen bewusst ohne eigene Instanz — Pruefliste, nicht
        // Loeschliste.
        _noInstanceHint.Dock = DockStyle.Top;
        _noInstanceHint.Height = 32;
        _noInstanceHint.ForeColor = SystemColors.GrayText;
        _noInstanceHint.Text =
            "Prüfliste, keine Löschliste: Manche Adapter laufen bewusst ohne eigene Instanz — " +
            "Socket-Backends wie ws/socketio (von admin/web genutzt) oder reine Abhängigkeiten.";

        _noInstanceCsv.Text = "Als CSV exportieren";
        _noInstanceCsv.Size = new Size(160, 24);
        _noInstanceCsv.Dock = DockStyle.Right;
        _noInstanceCsv.Click += (_, _) => ExportNoInstance();

        var noInstBar = TabLayout.TopBar(26);
        noInstBar.Controls.Add(_noInstanceCsv);

        _noInstance.Dock = DockStyle.Fill;
        _noInstance.View = View.Details;
        _noInstance.FullRowSelect = true;
        _noInstance.GridLines = true;
        _noInstance.HideSelection = false;
        _noInstance.MultiSelect = true;
        _noInstance.Columns.Add("Adapter", 260);
        _noInstance.Columns.Add("Version", 140);
        ListViewCopy.Attach(_noInstance);
        ListViewSort.Attach(_noInstance);

        // Reihenfolge so, dass von oben nach unten Header, Hinweis, CSV-Leiste, Liste stehen.
        bottom.Controls.Add(_noInstance);
        bottom.Controls.Add(noInstBar);
        bottom.Controls.Add(_noInstanceHint);
        bottom.Controls.Add(_noInstanceHeader);

        _placeholder.Dock = DockStyle.Fill;
        _placeholder.TextAlign = ContentAlignment.MiddleCenter;
        _placeholder.ForeColor = SystemColors.GrayText;
        _placeholder.Text = "Kein Backup geladen.";

        Controls.Add(_list);
        Controls.Add(_placeholder);
        Controls.Add(bottom);
        Controls.Add(head);
    }

    /// <summary>
    /// Setzt die Kopfleiste — mit oder ohne Warnzeile. Die Filterzeile rückt um die Höhe
    /// der Warnzeile nach unten, damit dort kein Loch bleibt, wenn nichts zu melden ist.
    /// </summary>
    private void PositionHead(bool withWarning)
    {
        _limitWarning.Visible = withWarning;
        _limitWarning.Location = new Point(0, 76);

        var dy = withWarning ? WarningHeight : 0;
        _lblFilter.Location = new Point(0, 80 + dy);
        _filter.Location = new Point(46, 77 + dy);
        _count.Location = new Point(380, 80 + dy);
        _csv.Location = TabLayout.RightAligned(160, 76 + dy);
        _head.Height = HeadHeight + dy;
    }

    public void SetAvailable(bool available)
    {
        _placeholder.Visible = !available;
        if (!available)
        {
            _placeholder.Text = _data is null
                ? "Kein Backup geladen.\r\n\r\nBitte oben eine Datei öffnen oder hineinziehen."
                : "Für die Übersicht wird ein Voll-Backup benötigt.\r\n\r\n" +
                  "Die geladene Datei enthält nur Skripte —\r\n" +
                  "verfügbar ist damit nur der Tab „Skripte\".";
            _placeholder.BringToFront();
        }
    }

    public void SetData(BackupData data)
    {
        _data = data;
        _sortColumn = -1;

        if (data.Kind != BackupKind.Full)
        {
            _header.Text = "";
            _metrics.Text = "";
            PositionHead(false);
            _list.Items.Clear();
            _adaptersWithoutInstance = new List<AdapterWithoutInstance>();
            _noInstance.Items.Clear();
            return;
        }

        _header.Text = OverviewPresenter.HeaderText(data);

        // Der Presenter trennt Zeilen mit \n; WinForms-Labels brauchen \r\n.
        _metrics.Text = OverviewPresenter.MetricsText(data).Replace("\n", "\r\n");

        var warning = OverviewPresenter.ObjectLimitWarning(data);
        _limitWarning.Text = warning?.Replace("\n", "\r\n") ?? "";
        PositionHead(warning is not null);

        _adaptersWithoutInstance = OrphanAnalyzer.FindAdaptersWithoutInstance(data);
        FillNoInstance();

        ApplyFilter();
    }

    private void FillNoInstance()
    {
        _noInstance.BeginUpdate();
        _noInstance.Items.Clear();
        foreach (var a in _adaptersWithoutInstance)
            _noInstance.Items.Add(new ListViewItem(new[] { a.Adapter, a.Version }));
        _noInstance.EndUpdate();

        _noInstanceHeader.Text = OverviewPresenter.NoInstanceHeader(_adaptersWithoutInstance.Count);
    }

    private void ExportNoInstance()
    {
        CsvExport.Save(this, "adapter-ohne-instanz.csv",
            OverviewPresenter.NoInstanceColumns,
            _adaptersWithoutInstance.Select(OverviewPresenter.Row));
    }

    private void ExportCsv()
    {
        CsvExport.Save(this, "adapter-instanzen.csv",
            OverviewPresenter.InstanceColumns,
            _filtered.Select(OverviewPresenter.Row));
    }

    private void ApplyFilter()
    {
        if (_data is null) return;

        _filtered = OverviewPresenter.Filter(_data, _filter.Text);

        ApplySort();
        FillList();

        _count.Text = OverviewPresenter.CountText(_filtered.Count, _data.Instances.Count);
    }

    private void FillList()
    {
        _list.BeginUpdate();
        _list.Items.Clear();

        foreach (var i in _filtered)
        {
            var item = new ListViewItem(OverviewPresenter.DisplayRow(i)) { Tag = i };

            if (!i.Enabled) item.ForeColor = SystemColors.GrayText;

            // Nur die Objektzahl wird hervorgehoben, nicht die ganze Zeile: Die Instanz ist
            // in Ordnung, allein ihr Objektbestand ist zu groß. Sobald einzelne Zellen ihre
            // eigene Farbe bekommen, erben sie die Zeilenfarbe nicht mehr — deshalb wird
            // sie hier zuerst auf alle Zellen übertragen.
            if (i.OverObjectLimit)
            {
                item.UseItemStyleForSubItems = false;
                foreach (ListViewItem.ListViewSubItem cell in item.SubItems)
                    cell.ForeColor = item.ForeColor;

                var objects = item.SubItems[Array.IndexOf(OverviewPresenter.InstanceColumns, "Objekte")];
                objects.ForeColor = Color.DarkOrange;
                objects.Font = new Font(_list.Font, FontStyle.Bold);
            }

            _list.Items.Add(item);
        }

        _list.EndUpdate();
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
        _filtered = OverviewPresenter.Sort(_filtered, _sortColumn, _sortAscending);
}

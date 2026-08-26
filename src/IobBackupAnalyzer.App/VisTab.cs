using System.Diagnostics;
using IobBackupAnalyzer.Core;

namespace IobBackupAnalyzer.App;

/// <summary>
/// Auflistung der in VIS verwendeten Datenpunkte, getrennt nach VIS 1 (vis.0) und
/// VIS 2 (vis-2.0), samt Export eines ganzen VIS-Projekts als ZIP für den Projektimport.
/// </summary>
public sealed class VisTab : UserControl
{
    private readonly Label _summary = new();
    private readonly TextBox _filter = new();
    private readonly ComboBox _scope = new();
    private readonly Label _count = new();
    private readonly Button _csv = new();
    private readonly Label _zipIntro = new();
    private readonly ComboBox _project = new();
    private readonly CheckBox _assets = new();
    private readonly Button _zip = new();
    private readonly ListView _list = new();
    private readonly ListView _usages = new();
    private readonly Label _usageHeader = new();
    private readonly Label _placeholder = new();

    // Untertab "Widget-Sätze"
    private readonly ListView _setList = new();
    private readonly Label _setCount = new();
    private readonly Button _setCsv = new();
    private readonly ListView _setHits = new();
    private readonly Label _setHitHeader = new();
    private List<WidgetSetRow> _sets = new();

    private BackupData? _data;
    private List<VisDatapoint> _all = new();
    private List<VisDatapoint> _filtered = new();
    private List<VisProjectExporter.VisProject> _projects = new();

    private int _sortColumn = -1;
    private bool _sortAscending = true;

    public VisTab()
    {
        BuildUi();
    }

    private void BuildUi()
    {
        Padding = new Padding(8);

        var head = TabLayout.TopBar(122);

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

        // Zweite Zeile: ein ganzes VIS-Projekt als ZIP, wie es der Projektimport erwartet.
        // Das ist der Weg, eine gelöschte View zurückzuholen, ohne das Backup einzuspielen.
        _zipIntro.Location = new Point(0, 86);
        _zipIntro.Size = new Size(330, 20);
        _zipIntro.ForeColor = SystemColors.GrayText;

        _project.Location = new Point(336, 83);
        _project.Size = new Size(300, 24);
        _project.DropDownStyle = ComboBoxStyle.DropDownList;
        // Die Beschriftung kommt aus dem Presenter-Record, damit beide Oberflächen
        // dieselbe Zeile zeigen.
        _project.DisplayMember = nameof(VisProjectExporter.VisProject.Label);
        _project.SelectedIndexChanged += (_, _) => UpdateZipState();

        _assets.Text = VisPresenter.ZipAssetsLabel;
        _assets.Location = new Point(646, 85);
        _assets.Size = new Size(170, 22);
        _assets.Checked = true;

        _zip.Text = VisPresenter.ZipButtonLabel;
        _zip.Size = new Size(210, 26);
        _zip.Location = TabLayout.RightAligned(210, 83);
        _zip.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _zip.Click += (_, _) => ExportZip();

        head.Controls.AddRange(new Control[]
            { _summary, lbl, _filter, _scope, _count, _csv, _zipIntro, _project, _assets, _zip });

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

        // Zwei Untertabs wie im Tab "Verwaiste Datenpunkte": Die Datenpunktliste beantwortet
        // "welcher Wert haengt wo", die Satzliste "welchen Baukasten brauche ich noch".
        var tabs = new TabControl { Dock = DockStyle.Fill };

        var seiteDp = new TabPage("Datenpunkte") { Padding = new Padding(6), UseVisualStyleBackColor = true };
        seiteDp.Controls.Add(split);
        seiteDp.Controls.Add(head);

        tabs.TabPages.Add(seiteDp);
        tabs.TabPages.Add(BuildWidgetSets());

        Controls.Add(tabs);
        Controls.Add(_placeholder);
    }

    /// <summary>
    /// Der Untertab "Widget-Sätze": welcher Baukasten in welcher Projektfassung steckt.
    /// Der Warnhinweis darueber ist Teil der Aussage, siehe <see cref="WidgetSetAnalyzer.Warning"/>.
    /// </summary>
    private TabPage BuildWidgetSets()
    {
        var page = new TabPage("Widget-Sätze") { Padding = new Padding(6), UseVisualStyleBackColor = true };

        var warn = new Label
        {
            Dock = DockStyle.Top,
            Height = 92,
            Text = WidgetSetAnalyzer.Warning.Replace("\n", "\r\n"),
            ForeColor = SystemColors.GrayText,
            Padding = new Padding(2, 4, 2, 6)
        };

        var bar = TabLayout.TopBar(40);

        _setCount.Location = new Point(0, 11);
        _setCount.Size = new Size(600, 20);
        _setCount.ForeColor = SystemColors.GrayText;

        _setCsv.Text = "Als CSV exportieren";
        _setCsv.Size = new Size(160, 26);
        _setCsv.Location = new Point(620, 7);
        _setCsv.Click += (_, _) => CsvExport.Save(this, "widget-saetze.csv",
            VisPresenter.WidgetSetCsvColumns, _sets.Select(VisPresenter.WidgetSetCsvRow));

        bar.Controls.AddRange(new Control[] { _setCount, _setCsv });
        bar.SizeChanged += (_, _) =>
        {
            _setCsv.Left = bar.ClientSize.Width - _setCsv.Width - 4;
            _setCount.Width = Math.Max(60, _setCsv.Left - 8);
        };

        _setList.Dock = DockStyle.Fill;
        _setList.View = View.Details;
        _setList.FullRowSelect = true;
        _setList.GridLines = true;
        _setList.HideSelection = false;
        _setList.MultiSelect = false;
        _setList.SelectedIndexChanged += (_, _) => ShowSetHits();
        _setList.Columns.Add("Widget-Satz", 260);
        _setList.Columns.Add("Instanz", 240);
        _setList.Columns.Add("Projekt", 110);
        _setList.Columns.Add("Widgets", 90);
        _setList.Columns.Add("Dateiverweise", 120);
        _setList.Columns.Add("Befund", 420);
        ListViewCopy.Attach(_setList);
        ListViewSort.Attach(_setList);

        // Unterer Bereich: wo der gewaehlte Satz steckt. Ohne ihn beantwortet die Liste nur
        // "wie viele", nicht "wo" — und das ist die Frage vor dem Aufraeumen.
        _setHitHeader.Dock = DockStyle.Top;
        _setHitHeader.Height = 22;
        _setHitHeader.Padding = new Padding(2, 3, 2, 0);
        _setHitHeader.Font = new Font(Font, FontStyle.Bold);
        _setHitHeader.Text = VisPresenter.WidgetSetHitHeader(null, 0);

        _setHits.Dock = DockStyle.Fill;
        _setHits.View = View.Details;
        _setHits.FullRowSelect = true;
        _setHits.GridLines = true;
        _setHits.HideSelection = false;
        _setHits.MultiSelect = true;
        _setHits.Columns.Add("VIS", 60);
        _setHits.Columns.Add("View", 220);
        _setHits.Columns.Add("Widget", 130);
        _setHits.Columns.Add("Art", 120);
        _setHits.Columns.Add("Fundstelle", 700);
        ListViewCopy.Attach(_setHits);
        ListViewSort.Attach(_setHits);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            Panel1MinSize = 120,
            Panel2MinSize = 100
        };

        var splitInit = false;
        split.SizeChanged += (_, _) =>
        {
            if (splitInit || split.Height < 300) return;
            split.SplitterDistance = split.Height * 3 / 5;
            splitInit = true;
        };

        split.Panel1.Controls.Add(_setList);
        split.Panel2.Controls.Add(_setHits);
        split.Panel2.Controls.Add(_setHitHeader);

        page.Controls.Add(split);
        page.Controls.Add(bar);
        page.Controls.Add(warn);
        return page;
    }

    /// <summary>Die Fundstellen des gewaehlten Satzes, begrenzt auf die Anzeigegrenze.</summary>
    private void ShowSetHits()
    {
        var gewaehlt = _setList.SelectedItems.Count > 0
            ? _setList.SelectedItems[0].Tag as WidgetSetRow
            : null;

        _setHits.BeginUpdate();
        _setHits.Items.Clear();

        var gezeigt = 0;
        if (gewaehlt is not null)
        {
            foreach (var h in gewaehlt.Hits.Take(VisPresenter.WidgetSetHitLimit))
            {
                _setHits.Items.Add(new ListViewItem(VisPresenter.WidgetSetHitRow(h)));
                gezeigt++;
            }
        }

        _setHits.EndUpdate();
        _setHitHeader.Text = VisPresenter.WidgetSetHitHeader(gewaehlt, gezeigt);
    }

    private void FillWidgetSets()
    {
        _setList.BeginUpdate();
        _setList.Items.Clear();

        foreach (var s in _sets)
        {
            var item = new ListViewItem(VisPresenter.WidgetSetRow(s)) { Tag = s };

            item.ForeColor = VisPresenter.WidgetSetEmphasis(s) switch
            {
                RowEmphasis.Muted => SystemColors.GrayText,
                RowEmphasis.Warn => Color.DarkOrange,
                RowEmphasis.Problem => Color.Firebrick,
                _ => SystemColors.ControlText
            };

            _setList.Items.Add(item);
        }

        _setList.EndUpdate();
        _setCount.Text = VisPresenter.WidgetSetCount(_sets);
        ShowSetHits();
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

    public void SetData(BackupData data, AnalysisResults? fertig = null)
    {
        _data = data;
        _sortColumn = -1;

        if (data.Kind != BackupKind.Full)
        {
            _all = new List<VisDatapoint>();
            _list.Items.Clear();
            _summary.Text = "";
            FillProjects();
            return;
        }

        if (fertig?.Vis is { } vorberechnet)
        {
            _all = vorberechnet;
        }
        else
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                _all = VisAnalyzer.Analyze(data);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        FillProjects();

        _sets = WidgetSetAnalyzer.Analyze(data);
        FillWidgetSets();

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

    // ------------------------------------------------- Projekt als ZIP für den Import

    /// <summary>Füllt die Projektauswahl aus dem Dateibaum des Backups.</summary>
    private void FillProjects()
    {
        _projects = _data is null
            ? new List<VisProjectExporter.VisProject>()
            : VisProjectExporter.FindProjects(_data);

        _project.Items.Clear();
        _project.Items.AddRange(_projects.Cast<object>().ToArray());
        if (_projects.Count > 0) _project.SelectedIndex = 0;

        _zipIntro.Text = VisPresenter.ZipIntro(_projects.Count);
        UpdateZipState();
    }

    private void UpdateZipState()
    {
        var project = _project.SelectedItem as VisProjectExporter.VisProject;

        _project.Enabled = _projects.Count > 0;
        _zip.Enabled = project is not null;
        // Ohne Beiwerk gibt es nichts zum Mitnehmen — der Schalter bliebe eine leere Zusage.
        _assets.Enabled = project is { } p && p.Assets.Count > 0;
    }

    /// <summary>
    /// Schreibt das gewählte VIS-Projekt als ZIP, wie sie der Projektimport von VIS
    /// erwartet: der Inhalt des Projektordners flach in der Wurzel.
    /// </summary>
    private void ExportZip()
    {
        if (_data is null || _project.SelectedItem is not VisProjectExporter.VisProject project)
            return;

        using var dlg = new SaveFileDialog
        {
            Title = "VIS-Projekt als ZIP speichern",
            Filter = "ZIP-Datei (*.zip)|*.zip|Alle Dateien (*.*)|*.*",
            FileName = project.SuggestedFileName(_data.CreatedAt),
            DefaultExt = "zip",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            Cursor = Cursors.WaitCursor;
            var result = VisProjectExporter.Export(_data, project, dlg.FileName, _assets.Checked);
            Cursor = Cursors.Default;

            var msg = VisPresenter.ZipSummary(_data, project, result).Replace("\n", "\r\n");
            var problems = result.Files == 0 || !result.ViewsIncluded
                        || result.Errors.Count > 0 || result.Missing.Count > 0;

            MessageBox.Show(this, msg, "VIS-Projekt exportiert", MessageBoxButtons.OK,
                problems ? MessageBoxIcon.Warning : MessageBoxIcon.Information);

            // Den Ordner öffnen und die ZIP darin markieren — sie soll gleich in den
            // Projektimport gezogen werden.
            if (result.Files > 0)
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{result.ZipPath}\"")
                    { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Cursor = Cursors.Default;
            Program.LogError("VIS-Projekt-Export", ex);
            MessageBox.Show(this, "Der Export ist fehlgeschlagen:\r\n\r\n" + ex.Message
                + "\r\n\r\nDetails in:\r\n" + Program.ErrorLogPath,
                "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

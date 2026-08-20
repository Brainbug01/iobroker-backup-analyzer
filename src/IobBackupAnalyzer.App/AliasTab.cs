using IobBackupAnalyzer.Core;

namespace IobBackupAnalyzer.App;

/// <summary>
/// Alias-Übersicht: jeder Alias mit seinem Lese- und Schreibziel und der Angabe, ob diese
/// Ziele im Backup noch existieren. Deckt kaputte Aliasse auf. Zusätzlich zeigt der
/// Detailbereich die Konvertierungsfunktionen des gewählten Alias und erzeugt auf Wunsch
/// einen Konverter-Vorschlag aus der Wertetabelle des Ziel-Datenpunkts.
/// </summary>
public sealed class AliasTab : UserControl
{
    private readonly Label _summary = new();
    private readonly TextBox _filter = new();
    private readonly ComboBox _scope = new();
    private readonly Label _count = new();
    private readonly Button _csv = new();
    private readonly ListView _list = new();
    private readonly Label _placeholder = new();

    // Detailbereich unten
    private readonly Label _detailHeader = new();
    private readonly TextBox _convRead = new();
    private readonly TextBox _convWrite = new();
    private readonly Button _generate = new();
    private readonly TextBox _genRead = new();
    private readonly TextBox _genWrite = new();
    private readonly Label _genNote = new();

    private BackupData? _data;
    private List<AliasRow> _all = new();
    private Dictionary<string, IobObject> _byId = new(StringComparer.Ordinal);

    public AliasTab()
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
        _filter.PlaceholderText = "Alias-ID oder Ziel …";
        _filter.TextChanged += (_, _) => Fill();

        _scope.Location = new Point(336, 48);
        _scope.Size = new Size(260, 24);
        _scope.DropDownStyle = ComboBoxStyle.DropDownList;
        // Reihenfolge und Beschriftung kommen aus dem Presenter, damit die Auswahl in
        // beiden Oberflächen dieselbe ist — der Index wird direkt auf AliasScope gecastet.
        _scope.Items.AddRange(AliasPresenter.ScopeLabels.Cast<object>().ToArray());
        _scope.SelectedIndex = 0;
        _scope.SelectedIndexChanged += (_, _) => Fill();

        _count.Location = new Point(610, 51);
        _count.Size = new Size(310, 20);
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
        _list.MultiSelect = true;
        _list.Columns.Add("Alias-ID", 300);
        _list.Columns.Add("Name", 150);
        _list.Columns.Add("Lese-Ziel", 300);
        _list.Columns.Add("Ziel vorhanden", 100);
        _list.Columns.Add("Schreib-Ziel (abweichend)", 260);
        _list.Columns.Add("Schreib-Ziel vorhanden", 130);
        _list.Columns.Add("Konverter", 80);
        _list.SelectedIndexChanged += (_, _) => ShowDetails();
        ListViewCopy.Attach(_list);
        ListViewSort.Attach(_list);

        // ---------- Detailbereich ----------
        _detailHeader.Dock = DockStyle.Top;
        _detailHeader.Height = 24;
        _detailHeader.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        _detailHeader.Text = AliasPresenter.DetailHeader(null);
        _detailHeader.TextAlign = ContentAlignment.MiddleLeft;

        var mono = new Font("Consolas", 9F);
        ConfigureCodeBox(_convRead, mono);
        ConfigureCodeBox(_convWrite, mono);
        ConfigureCodeBox(_genRead, mono);
        ConfigureCodeBox(_genWrite, mono);

        _generate.Text = "Konverter-Vorschlag aus Ziel-Datenpunkt erzeugen";
        _generate.AutoSize = false;
        _generate.Height = 28;
        _generate.Width = 340;
        _generate.Anchor = AnchorStyles.Left;
        _generate.Enabled = false;
        _generate.Click += (_, _) => GenerateForSelected();

        _genNote.Dock = DockStyle.Fill;
        _genNote.ForeColor = SystemColors.GrayText;
        _genNote.TextAlign = ContentAlignment.MiddleLeft;

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            Padding = new Padding(0, 4, 0, 0)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 5; i++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        AddGridRow(grid, 0, "Konverter Lesen (im Backup):", _convRead);
        AddGridRow(grid, 1, "Konverter Schreiben (im Backup):", _convWrite);
        grid.Controls.Add(new Label { Text = "", Anchor = AnchorStyles.Left }, 0, 2);
        grid.Controls.Add(_generate, 1, 2);
        AddGridRow(grid, 3, "Vorschlag Lesen:", _genRead);
        AddGridRow(grid, 4, "Vorschlag Schreiben:", _genWrite);
        grid.Controls.Add(new Label { Text = "Hinweis:", Anchor = AnchorStyles.Left, ForeColor = SystemColors.GrayText }, 0, 5);
        grid.Controls.Add(_genNote, 1, 5);

        var detail = new Panel { Dock = DockStyle.Fill };
        detail.Controls.Add(grid);
        detail.Controls.Add(_detailHeader);

        // MinSizes erst setzen, wenn genug Höhe da ist (siehe CleanupScriptDialog): am
        // ungesizten Control würde ein großes Panel2MinSize sofort werfen.
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal
        };
        var splitInit = false;
        split.SizeChanged += (_, _) =>
        {
            if (splitInit) return;
            const int p1min = 140, p2min = 150;
            if (split.Height < p1min + p2min + split.SplitterWidth + 20) return;   // noch zu klein
            split.Panel1MinSize = p1min;
            split.Panel2MinSize = p2min;
            split.SplitterDistance = Math.Clamp(split.Height * 3 / 5, p1min, split.Height - p2min - split.SplitterWidth);
            splitInit = true;
        };
        split.Panel1.Controls.Add(_list);
        split.Panel2.Controls.Add(detail);

        _placeholder.Dock = DockStyle.Fill;
        _placeholder.TextAlign = ContentAlignment.MiddleCenter;
        _placeholder.ForeColor = SystemColors.GrayText;
        _placeholder.Text = "Kein Backup geladen.";

        Controls.Add(split);
        Controls.Add(_placeholder);
        Controls.Add(head);
    }

    private static void ConfigureCodeBox(TextBox box, Font mono)
    {
        box.Dock = DockStyle.Fill;
        box.ReadOnly = true;
        box.Font = mono;
        box.BackColor = SystemColors.Window;
        box.Margin = new Padding(0, 3, 0, 3);
    }

    private static void AddGridRow(TableLayoutPanel grid, int row, string caption, Control value)
    {
        grid.Controls.Add(new Label
        {
            Text = caption,
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            Margin = new Padding(0, 8, 6, 0)
        }, 0, row);
        grid.Controls.Add(value, 1, row);
    }

    public void SetAvailable(bool available)
    {
        _placeholder.Visible = !available;
        if (!available)
        {
            _placeholder.Text = _data is null
                ? "Kein Backup geladen.\r\n\r\nBitte oben eine Datei öffnen oder hineinziehen."
                : "Für die Alias-Übersicht wird ein Voll-Backup benötigt.\r\n\r\n" +
                  "Die geladene Datei enthält nur Skripte.";
            _placeholder.BringToFront();
        }
    }

    public void SetData(BackupData data)
    {
        _data = data;

        if (data.Kind != BackupKind.Full)
        {
            _all = new List<AliasRow>();
            _byId = new Dictionary<string, IobObject>(StringComparer.Ordinal);
            _list.Items.Clear();
            _summary.Text = "";
            ClearDetails();
            return;
        }

        Cursor = Cursors.WaitCursor;
        try
        {
            _all = AliasAnalyzer.Analyze(data);
            // Ziel-Datenpunkte einmal nachschlagbar machen (für den Generator).
            _byId = new Dictionary<string, IobObject>(StringComparer.Ordinal);
            foreach (var o in data.Objects) _byId[o.Id] = o;
        }
        finally
        {
            Cursor = Cursors.Default;
        }

        _summary.Text = AliasPresenter.SummaryText(_all);

        Fill();
        ClearDetails();
    }

    private void Fill()
    {
        if (_data is null) return;

        var rows = CurrentRows();

        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var a in rows)
        {
            var item = new ListViewItem(AliasPresenter.DisplayRow(a)) { Tag = a };

            if (a.Broken) item.ForeColor = Color.Firebrick;

            _list.Items.Add(item);
        }
        _list.EndUpdate();

        _count.Text = AliasPresenter.CountText(rows.Count, _all.Count);
    }

    /// <summary>Die aktuell gefilterte Menge — für Anzeige und CSV-Export.</summary>
    private List<AliasRow> CurrentRows() =>
        AliasPresenter.Filter(_all, (AliasScope)_scope.SelectedIndex, _filter.Text);

    // ---------------------------------------------------------------- Detailbereich

    private AliasRow? SelectedRow =>
        _list.SelectedItems.Count > 0 && _list.SelectedItems[0].Tag is AliasRow a ? a : null;

    private void ShowDetails()
    {
        var a = SelectedRow;
        if (a is null)
        {
            ClearDetails();
            return;
        }

        _detailHeader.Text = AliasPresenter.DetailHeader(a);
        _convRead.Text = a.ConverterRead;
        _convWrite.Text = a.ConverterWrite;

        // Vorschlagsfelder erst nach Knopfdruck füllen.
        _genRead.Text = "";
        _genWrite.Text = "";
        _genNote.Text = "";
        _generate.Enabled = true;
    }

    private void ClearDetails()
    {
        _detailHeader.Text = AliasPresenter.DetailHeader(null);
        _convRead.Text = "";
        _convWrite.Text = "";
        _genRead.Text = "";
        _genWrite.Text = "";
        _genNote.Text = "";
        _generate.Enabled = false;
    }

    private void GenerateForSelected()
    {
        var a = SelectedRow;
        if (a is null) return;

        // Der Lese-Konverter bezieht sich auf das Lese-Ziel — von dort kommt die Wertetabelle.
        _byId.TryGetValue(a.ReadTarget, out var target);
        var result = ConverterGenerator.Generate(target);

        _genRead.Text = result.Read;
        _genWrite.Text = result.Write;
        _genNote.Text = result.Note;
        _genNote.ForeColor = result.CanGenerate ? SystemColors.ControlText : Color.Firebrick;
    }

    private void ExportCsv()
    {
        CsvExport.Save(this, "alias-uebersicht.csv",
            AliasPresenter.CsvColumns,
            CurrentRows().Select(AliasPresenter.Row));
    }
}

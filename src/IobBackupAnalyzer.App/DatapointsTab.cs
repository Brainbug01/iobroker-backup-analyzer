using IobBackupAnalyzer.Core;

namespace IobBackupAnalyzer.App;

/// <summary>
/// Tab „Datenpunkte": Suche über ID und Name, darunter der vollständige Wert zum Kopieren.
///
/// Der Zuschnitt folgt dem Zweck (siehe <see cref="DatapointPresenter"/>): Es geht darum, an
/// <b>einen bestimmten</b> Wert heranzukommen, nicht darum, den Objektbaum zu durchstöbern.
/// Deshalb Suchfeld statt Baum und ein großes Wertfeld statt vieler Spalten.
/// </summary>
internal sealed class DatapointsTab : UserControl
{
    private readonly TextBox _filter = new();
    private readonly Label _count = new();
    private readonly Button _csv = new();
    private readonly ListView _list = new();
    private readonly Label _placeholder = new();

    private readonly Label _definition = new();
    private readonly Label _valueInfo = new();
    private readonly TextBox _value = new();
    private readonly Button _copy = new();
    private readonly ToolTip _tips = new();

    private List<DatapointHit> _all = new();
    private List<DatapointHit> _shown = new();

    public DatapointsTab()
    {
        BuildUi();
    }

    private void BuildUi()
    {
        Dock = DockStyle.Fill;
        Padding = new Padding(8);

        var bar = TabLayout.TopBar(40);

        var lbl = new Label { Text = "Suche:", Location = new Point(0, 11), Size = new Size(48, 20) };
        _filter.Location = new Point(50, 8);
        _filter.Size = new Size(340, 24);
        _filter.PlaceholderText = "Datenpunkt-ID oder Name …";
        _filter.TextChanged += (_, _) => ApplyFilter();
        _tips.SetToolTip(_filter, DatapointPresenter.Hint);

        _count.Location = new Point(400, 11);
        _count.Size = new Size(420, 20);
        _count.ForeColor = SystemColors.GrayText;

        _csv.Text = "Als CSV exportieren";
        _csv.Size = new Size(160, 26);
        _csv.Location = new Point(840, 7);
        _csv.Click += (_, _) => Export();

        bar.Controls.AddRange(new Control[] { lbl, _filter, _count, _csv });

        // Rechtsbündig ohne Anchor — wie in den übrigen Tabs, siehe OrphansTab.
        bar.SizeChanged += (_, _) =>
        {
            _csv.Left = bar.ClientSize.Width - _csv.Width - 4;
            _count.Width = Math.Max(60, _csv.Left - _count.Location.X - 8);
        };

        // ---------- Unterer Bereich: der eigentliche Zweck des Tabs ----------

        var detail = new Panel { Dock = DockStyle.Bottom, Height = 300, Padding = new Padding(0, 8, 0, 0) };

        // Drei Zeilen hoch und umbrechend: Der Datenpunktname steht hier vollständig, und
        // manche Adapter legen ganze Sätze als Namen ab (im Testbackup bis 583 Zeichen).
        // Einzeilig wäre gerade der Teil abgeschnitten, wegen dem die Zeile gefunden wurde.
        _definition.Dock = DockStyle.Top;
        _definition.Height = 56;
        _definition.AutoEllipsis = true;
        _definition.Text = "";

        _valueInfo.Dock = DockStyle.Top;
        _valueInfo.Height = 20;
        _valueInfo.ForeColor = SystemColors.GrayText;

        var valueBar = new Panel { Dock = DockStyle.Top, Height = 32 };
        _copy.Text = "Wert kopieren";
        _copy.Size = new Size(140, 26);
        _copy.Location = new Point(0, 3);
        _copy.Enabled = false;
        _copy.Click += (_, _) => CopyValue();
        _tips.SetToolTip(_copy, "Kopiert den vollständigen Wert in die Zwischenablage.");
        valueBar.Controls.Add(_copy);

        // Schreibgeschützt, aber auswählbar: Der Wert soll herausgeholt, nicht bearbeitet
        // werden. Feste Schrift, weil eingerücktes JSON sonst nicht als Struktur lesbar ist.
        _value.Dock = DockStyle.Fill;
        _value.Multiline = true;
        _value.ReadOnly = true;
        _value.ScrollBars = ScrollBars.Both;
        _value.WordWrap = false;
        _value.Font = new Font(FontFamily.GenericMonospace, 9f);
        _value.BackColor = SystemColors.Window;

        detail.Controls.Add(_value);
        detail.Controls.Add(valueBar);
        detail.Controls.Add(_valueInfo);
        detail.Controls.Add(_definition);

        // ---------- Trefferliste ----------

        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.GridLines = true;
        _list.HideSelection = false;
        _list.MultiSelect = false;
        _list.Columns.Add("Datenpunkt-ID", 420);
        _list.Columns.Add("Name", 200);
        _list.Columns.Add("Typ", 130);
        _list.Columns.Add("Rolle", 170);
        _list.Columns.Add("Zuletzt geändert", 190);
        _list.Columns.Add(OrphansPresenter.ValueColumn, 300);
        _list.SelectedIndexChanged += (_, _) => ShowSelected();
        ListViewCopy.Attach(_list);
        ListViewSort.Attach(_list);

        _placeholder.Dock = DockStyle.Fill;
        _placeholder.TextAlign = ContentAlignment.MiddleCenter;
        _placeholder.ForeColor = SystemColors.GrayText;
        _placeholder.Text = "Kein Backup geladen.";

        Controls.Add(_list);
        Controls.Add(_placeholder);
        Controls.Add(detail);
        Controls.Add(bar);
    }

    public void SetAvailable(bool available)
    {
        _placeholder.Visible = !available;
        if (!available)
        {
            _placeholder.Text = "Kein Voll-Backup geladen.\r\n\r\n" +
                                "Datenpunkte und ihre Werte stehen nur in einem vollständigen " +
                                "Backitup-Archiv.";
            _placeholder.BringToFront();
        }
    }

    public void SetData(BackupData data)
    {
        _all = DatapointPresenter.Build(data);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var gefiltert = DatapointPresenter.Filter(_all, _filter.Text);
        _shown = gefiltert.Take(DatapointPresenter.DisplayLimit).ToList();

        _list.BeginUpdate();
        _list.Items.Clear();

        foreach (var h in _shown)
        {
            var item = new ListViewItem(DatapointPresenter.DisplayRow(h)) { Tag = h };

            item.ForeColor = DatapointPresenter.Emphasis(h) switch
            {
                RowEmphasis.Muted => SystemColors.GrayText,
                RowEmphasis.Warn => Color.DarkOrange,
                _ => SystemColors.ControlText
            };

            _list.Items.Add(item);
        }

        _list.EndUpdate();

        _count.Text = DatapointPresenter.Count(gefiltert.Count, _all.Count,
                                               _filter.Text.Trim().Length > 0);
        ShowSelected();
    }

    private void ShowSelected()
    {
        var hit = _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as DatapointHit : null;

        _definition.Text = DatapointPresenter.Definition(hit);
        _valueInfo.Text = DatapointPresenter.ValueInfo(hit);

        // Windows-Zeilenenden: Eine mehrzeilige TextBox zeigt ein einzelnes \n sonst als
        // Kästchen statt als Umbruch — bei eingerücktem JSON wäre das der ganze Inhalt.
        _value.Text = DatapointPresenter.FullValue(hit).Replace("\r\n", "\n").Replace("\n", "\r\n");
        _copy.Enabled = _value.Text.Length > 0;
    }

    private void CopyValue()
    {
        if (_value.Text.Length == 0) return;

        try
        {
            Clipboard.SetText(_value.Text);
        }
        catch (Exception ex)
        {
            // Die Zwischenablage kann von einem anderen Programm belegt sein — das ist
            // ärgerlich, aber kein Grund, den Tab abstürzen zu lassen.
            Program.LogError("Wert kopieren", ex);
            MessageBox.Show(this, "Der Wert konnte nicht in die Zwischenablage gelegt werden:\r\n\r\n"
                                  + ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void Export() =>
        CsvExport.Save(this, "datenpunkte.csv", DatapointPresenter.CsvColumns,
            DatapointPresenter.Filter(_all, _filter.Text).Select(DatapointPresenter.Row));
}

using IobBackupAnalyzer.Core;

namespace IobBackupAnalyzer.App;

/// <summary>
/// Logging-Übersicht: welcher Datenpunkt wird von welcher Instanz geloggt und mit welchen
/// Optionen. Deckt alle Logging-Adapter ab (history, influxdb, sql, sourceanalytix …) in
/// jeder Instanznummer.
/// </summary>
public sealed class LoggingTab : UserControl
{
    private readonly Label _summary = new();
    private readonly TextBox _filter = new();
    private readonly ComboBox _scope = new();
    private readonly Label _count = new();
    private readonly Button _csv = new();
    private readonly ListView _list = new();
    private readonly Label _placeholder = new();

    private BackupData? _data;
    private List<LoggingRow> _all = new();

    public LoggingTab()
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
        _filter.PlaceholderText = "Datenpunkt-ID, Instanz oder Adapter …";
        _filter.TextChanged += (_, _) => Fill();

        _scope.Location = new Point(336, 48);
        _scope.Size = new Size(230, 24);
        _scope.DropDownStyle = ComboBoxStyle.DropDownList;
        // Reihenfolge und Beschriftung kommen aus dem Presenter, damit die Auswahl in
        // beiden Oberflächen dieselbe ist — der Index wird direkt auf LoggingScope gecastet.
        _scope.Items.AddRange(LoggingPresenter.ScopeLabels.Cast<object>().ToArray());
        _scope.SelectedIndex = 0;
        _scope.SelectedIndexChanged += (_, _) => Fill();

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
        _list.MultiSelect = true;
        _list.Columns.Add("Datenpunkt-ID", 380);
        _list.Columns.Add("Name", 170);
        _list.Columns.Add("Instanz", 120);
        _list.Columns.Add("Adapter", 110);
        _list.Columns.Add("Aktiv", 60);
        _list.Columns.Add("Nur bei Änderung", 120);
        _list.Columns.Add("Entprellung", 100, HorizontalAlignment.Right);
        _list.Columns.Add("Alias-Name", 160);
        ListViewCopy.Attach(_list);
        ListViewSort.Attach(_list);

        _placeholder.Dock = DockStyle.Fill;
        _placeholder.TextAlign = ContentAlignment.MiddleCenter;
        _placeholder.ForeColor = SystemColors.GrayText;
        _placeholder.Text = "Kein Backup geladen.";

        Controls.Add(_list);
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
                : "Für die Logging-Übersicht wird ein Voll-Backup benötigt.\r\n\r\n" +
                  "Die geladene Datei enthält nur Skripte.";
            _placeholder.BringToFront();
        }
    }

    public void SetData(BackupData data)
    {
        _data = data;

        if (data.Kind != BackupKind.Full)
        {
            _all = new List<LoggingRow>();
            _list.Items.Clear();
            _summary.Text = "";
            return;
        }

        Cursor = Cursors.WaitCursor;
        try
        {
            _all = LoggingAnalyzer.Analyze(data);
        }
        finally
        {
            Cursor = Cursors.Default;
        }

        _summary.Text = LoggingPresenter.SummaryText(_all).Replace("\n", "\r\n");

        Fill();
    }

    private void Fill()
    {
        if (_data is null) return;

        var rows = CurrentRows();

        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var r in rows)
        {
            var item = new ListViewItem(LoggingPresenter.DisplayRow(r));

            // Ein deaktivierter Logging-Eintrag ist der eigentliche Aufräumfall — grau
            // heben, damit er sich von aktiven abhebt.
            if (!r.Enabled) item.ForeColor = SystemColors.GrayText;

            _list.Items.Add(item);
        }
        _list.EndUpdate();

        _count.Text = LoggingPresenter.CountText(rows.Count, _all.Count);
    }

    private void ExportCsv() =>
        CsvExport.Save(this, "logging-uebersicht.csv",
            LoggingPresenter.CsvColumns,
            CurrentRows().Select(LoggingPresenter.Row));

    /// <summary>
    /// Die aktuell sichtbare Menge — Grundlage für Liste und CSV-Export gleichermaßen,
    /// damit exportiert wird, was man sieht.
    /// </summary>
    private List<LoggingRow> CurrentRows() =>
        LoggingPresenter.Filter(_all, (LoggingScope)_scope.SelectedIndex, _filter.Text);
}

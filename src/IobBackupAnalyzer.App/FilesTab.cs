using System.Diagnostics;
using IobBackupAnalyzer.Core;

namespace IobBackupAnalyzer.App;

/// <summary>
/// Die Dateien aus dem files/-Baum des Backups — das, was der Admin unter „Dateien" zeigt.
/// Liste mit Filter und Export; die Inhalte werden erst beim Export aus dem Archiv gelesen.
/// </summary>
public sealed class FilesTab : UserControl
{
    private readonly Label _summary = new();
    private readonly TextBox _filter = new();
    private readonly ComboBox _namespaces = new();
    private readonly Label _count = new();
    private readonly Button _btnExportSelected = new();
    private readonly Button _btnExportAll = new();
    private readonly Button _btnCsv = new();
    private readonly ListView _list = new();
    private readonly Label _placeholder = new();

    private BackupData? _data;
    private List<BackupFileInfo> _filtered = new();

    private int _sortColumn = -1;
    private bool _sortAscending = true;

    public FilesTab()
    {
        BuildUi();
    }

    private void BuildUi()
    {
        Padding = new Padding(8);

        var head = TabLayout.TopBar(112);

        _summary.Location = new Point(0, 0);
        _summary.Size = new Size(TabLayout.DesignWidth, 56);
        _summary.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        var lbl = new Label { Text = "Filter:", Location = new Point(0, 67), Size = new Size(44, 20) };
        _filter.Location = new Point(46, 64);
        _filter.Size = new Size(280, 24);
        _filter.PlaceholderText = "Dateiname oder Pfad …";
        _filter.TextChanged += (_, _) => ApplyFilter();

        _namespaces.Location = new Point(336, 64);
        _namespaces.Size = new Size(220, 24);
        _namespaces.DropDownStyle = ComboBoxStyle.DropDownList;
        _namespaces.SelectedIndexChanged += (_, _) => ApplyFilter();

        _count.Location = new Point(570, 67);
        _count.Size = new Size(200, 20);
        _count.ForeColor = SystemColors.GrayText;

        _btnCsv.Text = "Liste als CSV";
        _btnCsv.Size = new Size(140, 26);
        _btnCsv.Location = new Point(0, 0);
        _btnCsv.Click += (_, _) => ExportCsv();

        _btnExportSelected.Text = "Ausgewählte exportieren";
        _btnExportSelected.Size = new Size(180, 26);
        _btnExportSelected.Location = new Point(148, 0);
        _btnExportSelected.Click += (_, _) => Export(selectedOnly: true);

        _btnExportAll.Text = "Alle exportieren";
        _btnExportAll.Size = new Size(150, 26);
        _btnExportAll.Location = new Point(336, 0);
        _btnExportAll.Click += (_, _) => Export(selectedOnly: false);

        var buttonBar = new Panel
        {
            Location = TabLayout.RightAligned(490, 63),
            Size = new Size(490, 30),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        buttonBar.Controls.AddRange(new Control[] { _btnCsv, _btnExportSelected, _btnExportAll });

        head.Controls.AddRange(new Control[] { _summary, lbl, _filter, _namespaces, _count, buttonBar });

        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.GridLines = true;
        _list.HideSelection = false;
        _list.MultiSelect = true;
        _list.Columns.Add("Namensraum", 170);
        _list.Columns.Add("Pfad", 560);
        _list.Columns.Add("Größe", 90, HorizontalAlignment.Right);
        _list.Columns.Add("Typ", 100);
        _list.ColumnClick += OnColumnClick;

        ListViewCopy.Attach(_list);

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
                : "Die geladene Datei enthält keinen Dateibereich.\r\n\r\n" +
                  "Dateien liegen nur in einem vollständigen Backitup-Archiv.";
            _placeholder.BringToFront();
        }
    }

    public void SetData(BackupData data)
    {
        _data = data;
        _sortColumn = -1;

        _summary.Text = FilesPresenter.SummaryText(data).Replace("\n", "\r\n");

        _namespaces.Items.Clear();
        _namespaces.Items.AddRange(FilesPresenter.NamespaceChoices(data.Files).Cast<object>().ToArray());
        _namespaces.SelectedIndex = 0;

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (_data is null) return;

        _filtered = FilesPresenter.Filter(_data.Files, _namespaces.SelectedItem as string, _filter.Text);
        _filtered = FilesPresenter.Sort(_filtered, _sortColumn, _sortAscending);
        FillList();

        _count.Text = FilesPresenter.CountText(_filtered.Count, _data.Files.Count);
        _btnExportAll.Text = FilesPresenter.ExportAllLabel(_filtered.Count, _data.Files.Count);
    }

    private void FillList()
    {
        _list.BeginUpdate();
        _list.Items.Clear();

        foreach (var f in _filtered)
            _list.Items.Add(new ListViewItem(FilesPresenter.Row(f)) { Tag = f });

        _list.EndUpdate();
    }

    private void OnColumnClick(object? sender, ColumnClickEventArgs e)
    {
        if (e.Column == _sortColumn) _sortAscending = !_sortAscending;
        else { _sortColumn = e.Column; _sortAscending = true; }

        ListViewSort.ShowMarker(_list, _sortColumn, _sortAscending);
        ApplyFilter();
    }

    private void Export(bool selectedOnly)
    {
        if (_data is null) return;

        List<BackupFileInfo> toExport;
        if (selectedOnly)
        {
            toExport = _list.SelectedItems.Cast<ListViewItem>()
                            .Select(i => i.Tag).OfType<BackupFileInfo>().ToList();
            if (toExport.Count == 0)
            {
                MessageBox.Show(this, "Bitte zuerst mindestens eine Datei in der Liste auswählen.",
                    "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
        }
        else
        {
            // Bewusst die gefilterte Liste: Wer nach „vis-2.0" filtert, erwartet bei
            // „Alle exportieren" auch nur diese Dateien.
            toExport = _filtered;
        }

        if (toExport.Count == 0) return;

        using var dlg = new FolderBrowserDialog
        {
            Description = $"Zielordner für {toExport.Count} Datei(en) wählen",
            UseDescriptionForTitle = true
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            Cursor = Cursors.WaitCursor;
            var result = BackupFileExporter.Export(_data, toExport, dlg.SelectedPath);
            Cursor = Cursors.Default;

            var msg = FilesPresenter.ExportSummary(result).Replace("\n", "\r\n");
            var problems = result.Errors.Count > 0 || result.Missing.Count > 0;

            MessageBox.Show(this, msg, "Export abgeschlossen", MessageBoxButtons.OK,
                problems ? MessageBoxIcon.Warning : MessageBoxIcon.Information);

            Process.Start(new ProcessStartInfo(result.RootDir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Cursor = Cursors.Default;
            Program.LogError("Datei-Export", ex);
            MessageBox.Show(this, "Der Export ist fehlgeschlagen:\r\n\r\n" + ex.Message
                + "\r\n\r\nDetails in:\r\n" + Program.ErrorLogPath,
                "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportCsv() =>
        CsvExport.Save(this, "dateien.csv", FilesPresenter.Columns,
            _filtered.Select(FilesPresenter.Row));
}

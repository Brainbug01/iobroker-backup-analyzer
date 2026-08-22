using System.Diagnostics;
using System.Runtime.InteropServices;
using IobBackupAnalyzer.Core;

namespace IobBackupAnalyzer.App;

/// <summary>Säule 2: Skript-Extraktion.</summary>
public sealed class ScriptsTab : UserControl
{
    private readonly TextBox _search = new();
    private readonly ComboBox _searchMode = new();
    private readonly ComboBox _typeFilter = new();
    private readonly CheckBox _hideDisabled = new();
    private readonly CheckBox _onlyWithHints = new();
    private readonly CheckBox _withGeneratedJs = new();
    private readonly Label _count = new();

    private readonly ListView _list = new();
    private readonly TextBox _hints = new();
    private readonly TextBox _preview = new();
    private readonly RadioButton _showXml = new();
    private readonly RadioButton _showJs = new();
    private readonly Panel _viewSwitch = new();

    private readonly Button _btnClipboard = new();
    private readonly Button _btnExportSelected = new();
    private readonly Button _btnExportAll = new();
    private readonly Button _btnListCsv = new();
    private readonly Label _placeholder = new();

    private BackupData? _data;
    private List<ScriptInfo> _filtered = new();
    private ScriptInfo? _current;

    /// <summary>Sortierzustand je Spalte für den Klick-Sortierer.</summary>
    private int _sortColumn = -1;
    private bool _sortAscending = true;

    public ScriptsTab()
    {
        BuildUi();
    }

    private void BuildUi()
    {
        Padding = new Padding(8);

        // ---------- Filterleiste ----------
        // 94 statt 68: unter den Export-Knöpfen steht der Umschalter für das Dateiformat.
        var filterBar = TabLayout.TopBar(94);

        var lblSearch = new Label { Text = "Suche:", Location = new Point(0, 9), Size = new Size(48, 20) };
        _search.Location = new Point(50, 6);
        _search.Size = new Size(320, 24);
        _search.PlaceholderText = "Suchbegriff …";
        _search.TextChanged += (_, _) => ApplyFilter();

        _searchMode.Location = new Point(378, 6);
        _searchMode.Size = new Size(160, 24);
        _searchMode.DropDownStyle = ComboBoxStyle.DropDownList;
        _searchMode.Items.AddRange(ScriptsPresenter.SearchModeLabels.Cast<object>().ToArray());
        _searchMode.SelectedIndex = 0;
        _searchMode.SelectedIndexChanged += (_, _) => ApplyFilter();

        var lblType = new Label { Text = "Typ:", Location = new Point(552, 9), Size = new Size(32, 20) };
        _typeFilter.Location = new Point(586, 6);
        _typeFilter.Size = new Size(140, 24);
        _typeFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        _typeFilter.Items.AddRange(ScriptsPresenter.TypeLabels.Cast<object>().ToArray());
        _typeFilter.SelectedIndex = 0;
        _typeFilter.SelectedIndexChanged += (_, _) => ApplyFilter();

        _hideDisabled.Text = "Deaktivierte ausblenden";
        _hideDisabled.Location = new Point(740, 8);
        _hideDisabled.Size = new Size(180, 22);
        _hideDisabled.CheckedChanged += (_, _) => ApplyFilter();

        _onlyWithHints.Text = ScriptsPresenter.OnlyWithHintsLabel;
        _onlyWithHints.Location = new Point(926, 8);
        _onlyWithHints.Size = new Size(168, 22);
        _onlyWithHints.CheckedChanged += (_, _) => ApplyFilter();

        _count.Location = new Point(0, 40);
        _count.Size = new Size(500, 20);
        _count.ForeColor = SystemColors.GrayText;

        _btnClipboard.Text = "In Zwischenablage";
        _btnClipboard.Size = new Size(150, 26);
        _btnClipboard.Location = new Point(0, 0);
        _btnClipboard.Click += (_, _) => CopyToClipboard();

        _btnExportSelected.Text = "Ausgewählte exportieren";
        _btnExportSelected.Size = new Size(180, 26);
        _btnExportSelected.Location = new Point(158, 0);
        _btnExportSelected.Click += (_, _) => Export(selectedOnly: true);

        _btnExportAll.Text = "Alle exportieren";
        _btnExportAll.Size = new Size(150, 26);
        _btnExportAll.Location = new Point(346, 0);
        _btnExportAll.Click += (_, _) => Export(selectedOnly: false);

        // Datei-Export (oben) schreibt die Skripte selbst; dieser Knopf exportiert die
        // Liste als Tabelle (Name, Pfad, Typ, Status) für Excel & Co.
        _btnListCsv.Text = "Liste als CSV";
        _btnListCsv.Size = new Size(140, 26);
        _btnListCsv.Location = new Point(504, 0);
        _btnListCsv.Click += (_, _) => ExportListCsv();

        // Ausgeschaltet ist der Auslieferungszustand: je Skript genau eine Datei, so wie
        // sie auch in ioBroker liegt. Siehe ScriptsPresenter.GeneratedJsHint.
        _withGeneratedJs.Text = ScriptsPresenter.GeneratedJsLabel;
        _withGeneratedJs.Location = new Point(2, 32);
        _withGeneratedJs.Size = new Size(330, 22);
        _withGeneratedJs.Checked = false;

        var buttonBar = new Panel { Location = new Point(500, 36), Size = new Size(660, 56) };
        buttonBar.Controls.AddRange(new Control[]
        {
            _btnClipboard, _btnExportSelected, _btnExportAll, _btnListCsv, _withGeneratedJs
        });

        var tips = new ToolTip { AutoPopDelay = 20000 };
        tips.SetToolTip(_withGeneratedJs, ScriptsPresenter.GeneratedJsHint.Replace("\n", "\r\n"));
        tips.SetToolTip(_onlyWithHints, ScriptsPresenter.HintsHint.Replace("\n", "\r\n"));

        filterBar.Controls.AddRange(new Control[]
        {
            lblSearch, _search, _searchMode, lblType, _typeFilter, _hideDisabled,
            _onlyWithHints, _count, buttonBar
        });

        // ---------- Liste + Vorschau ----------
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            Panel1MinSize = 120,
            Panel2MinSize = 120
        };

        // SplitterDistance erst setzen, wenn das Control seine echte Höhe kennt —
        // vorher wirft WinForms, weil der Wert außerhalb der Panel-Mindestgrößen liegt.
        var splitterInitialized = false;
        split.SizeChanged += (_, _) =>
        {
            if (splitterInitialized || split.Height < 300) return;
            split.SplitterDistance = split.Height * 2 / 5;
            splitterInitialized = true;
        };

        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.MultiSelect = true;
        _list.HideSelection = false;
        _list.GridLines = true;
        _list.Columns.Add("Name", 220);
        _list.Columns.Add("ioBroker-Pfad", 400);
        _list.Columns.Add("Typ", 140);
        _list.Columns.Add("Status", 90);
        _list.Columns.Add("Hinweise", 320);
        _list.SelectedIndexChanged += (_, _) => ShowPreview();
        _list.ColumnClick += OnColumnClick;
        // Kopiert Listenwerte wie den ioBroker-Pfad. Der Quelltext des Skripts geht
        // weiterhin über den Button „In Zwischenablage".
        ListViewCopy.Attach(_list);
        // Sortiert wird hier auf der Datenliste (siehe ApplySort), damit die Reihenfolge
        // einen Filterwechsel überlebt — von ListViewSort kommt nur die Spaltenverschiebung.
        ListViewSort.EnableReorder(_list);

        _preview.Dock = DockStyle.Fill;
        _preview.Multiline = true;
        _preview.ReadOnly = true;
        _preview.ScrollBars = ScrollBars.Both;
        _preview.WordWrap = false;
        _preview.Font = new Font("Consolas", 9.5F);
        _preview.BackColor = Color.White;
        // Treffermarkierung der Codesuche bleibt sichtbar, auch wenn der Fokus im Suchfeld steht.
        _preview.HideSelection = false;

        // Die Befunde zum gewählten Skript — nur sichtbar, wenn es welche gibt. Bewusst
        // über der Vorschau und nicht in ihr: Der Vorschautext ist der unveränderte
        // Quelltext des Skripts und bleibt es auch.
        _hints.Dock = DockStyle.Top;
        _hints.Height = 96;
        _hints.Multiline = true;
        _hints.ReadOnly = true;
        _hints.ScrollBars = ScrollBars.Vertical;
        _hints.WordWrap = true;
        _hints.BorderStyle = BorderStyle.FixedSingle;
        _hints.BackColor = Color.FromArgb(255, 248, 225);
        _hints.ForeColor = Color.FromArgb(90, 60, 0);
        _hints.Visible = false;

        _showJs.Text = "Generiertes JavaScript";
        _showJs.Location = new Point(4, 3);
        _showJs.Size = new Size(170, 22);
        _showJs.Checked = true;
        _showJs.CheckedChanged += (_, _) => { if (_showJs.Checked) ShowPreview(); };

        _showXml.Text = "Blockly-XML";
        _showXml.Location = new Point(180, 3);
        _showXml.Size = new Size(130, 22);
        _showXml.CheckedChanged += (_, _) => { if (_showXml.Checked) ShowPreview(); };

        _viewSwitch.Dock = DockStyle.Top;
        _viewSwitch.Height = 28;
        _viewSwitch.Visible = false;
        _viewSwitch.Controls.AddRange(new Control[] { _showJs, _showXml });

        split.Panel1.Controls.Add(_list);
        split.Panel2.Controls.Add(_preview);
        split.Panel2.Controls.Add(_viewSwitch);
        split.Panel2.Controls.Add(_hints);

        _placeholder.Dock = DockStyle.Fill;
        _placeholder.TextAlign = ContentAlignment.MiddleCenter;
        _placeholder.ForeColor = SystemColors.GrayText;
        _placeholder.Text = "Kein Backup geladen.\r\n\r\nBitte oben eine Datei öffnen oder hineinziehen.";
        _placeholder.Visible = true;

        Controls.Add(split);
        Controls.Add(_placeholder);
        Controls.Add(filterBar);
    }

    public void SetAvailable(bool available)
    {
        _placeholder.Visible = !available;
        _placeholder.BringToFront();
    }

    public void SetData(BackupData data)
    {
        _data = data;
        _sortColumn = -1;
        ApplyFilter();
    }

    /// <summary>
    /// Wählt ein Skript anhand seiner ioBroker-ID aus — der Anlaufpunkt für den Doppelklick
    /// im Tab „Verwendung".
    ///
    /// Verdeckt die laufende Filterung das Skript (Suchbegriff, Typfilter, ausgeblendete
    /// deaktivierte Skripte), werden die Filter zurückgesetzt: Ein Sprung, der in einer
    /// leeren Liste endet, sähe wie ein Fehler aus.
    /// </summary>
    public bool SelectScript(string scriptId)
    {
        if (_data is null) return false;
        if (_data.Scripts.All(s => s.Id != scriptId)) return false;

        if (_filtered.All(s => s.Id != scriptId))
        {
            _search.Text = "";
            _typeFilter.SelectedIndex = 0;
            _hideDisabled.Checked = false;
            _onlyWithHints.Checked = false;
            ApplyFilter();
        }

        var index = _filtered.FindIndex(s => s.Id == scriptId);
        if (index < 0 || index >= _list.Items.Count) return false;

        _list.SelectedItems.Clear();
        var item = _list.Items[index];
        item.Selected = true;
        item.EnsureVisible();
        _list.Focus();
        return true;
    }

    // ---------------------------------------------------------------- Filter

    private void ApplyFilter()
    {
        if (_data is null) return;

        _filtered = ScriptsPresenter.Filter(_data.Scripts, _hideDisabled.Checked,
                                            _typeFilter.SelectedIndex, CurrentSearchMode, _search.Text,
                                            _onlyWithHints.Checked);
        ApplySort();
        FillList();

        _count.Text = ScriptsPresenter.CountText(_filtered.Count, _data, CurrentSearchMode, _search.Text);
        _btnExportAll.Text = ScriptsPresenter.ExportAllLabel(_filtered.Count, _data.Scripts.Count);
    }

    private ScriptSearchMode CurrentSearchMode =>
        (ScriptSearchMode)Math.Max(0, _searchMode.SelectedIndex);

    private void FillList()
    {
        _list.BeginUpdate();
        _list.Items.Clear();

        foreach (var s in _filtered)
        {
            var item = new ListViewItem(ScriptsPresenter.Row(s)) { Tag = s };

            item.ForeColor = ScriptsPresenter.Emphasis(s) switch
            {
                RowEmphasis.Muted => SystemColors.GrayText,
                RowEmphasis.Warn => Color.DarkOrange,
                RowEmphasis.Problem => Color.Firebrick,
                _ => SystemColors.ControlText
            };

            _list.Items.Add(item);
        }

        _list.EndUpdate();

        if (_list.Items.Count > 0) _list.Items[0].Selected = true;
        else ShowPreview();
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
        _filtered = ScriptsPresenter.Sort(_filtered, _sortColumn, _sortAscending);

    // ---------------------------------------------------------------- Vorschau

    private void ShowPreview()
    {
        if (_list.SelectedItems.Count == 0)
        {
            _current = null;
            _preview.Text = "";
            _viewSwitch.Visible = false;
            _hints.Visible = false;
            return;
        }

        _current = _list.SelectedItems[0].Tag as ScriptInfo;
        if (_current is null) return;

        var details = ScriptsPresenter.HintDetails(_current);
        _hints.Visible = details.Length > 0;
        _hints.Text = details.Replace("\r\n", "\n").Replace("\n", "\r\n");

        var hasXml = ScriptsPresenter.HasXmlView(_current);
        _viewSwitch.Visible = hasXml;
        if (!hasXml && _showXml.Checked) _showJs.Checked = true;

        var text = ScriptsPresenter.PreviewText(_current, _showXml.Checked);
        _preview.Text = text.Replace("\r\n", "\n").Replace("\n", "\r\n");

        // Bei aktiver Codesuche zum ersten Treffer springen und ihn markieren.
        // Wichtig: kein Focus() — das würde beim Tippen den Fokus aus dem Suchfeld reißen.
        // Die Markierung bleibt dank HideSelection = false auch ohne Fokus sichtbar.
        var term = _search.Text.Trim();
        if (CurrentSearchMode == ScriptSearchMode.Code && term.Length > 0)
        {
            var idx = _preview.Text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                _preview.Select(idx, term.Length);
                _preview.ScrollToCaret();
            }
        }
    }

    // ---------------------------------------------------------------- Aktionen

    private void CopyToClipboard()
    {
        if (_current is null)
        {
            MessageBox.Show(this, "Bitte zuerst ein Skript auswählen.", "Hinweis",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            var text = _preview.Text;
            if (text.Length == 0) return;
            Clipboard.SetText(text);
            ShowToast($"„{_current.Name}\" in die Zwischenablage kopiert.");
        }
        catch (ExternalException ex)
        {
            Program.LogError("Zwischenablage", ex);
            MessageBox.Show(this, "Die Zwischenablage ist gerade durch ein anderes Programm belegt.",
                "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void Export(bool selectedOnly)
    {
        if (_data is null) return;

        List<ScriptInfo> toExport;
        if (selectedOnly)
        {
            toExport = _list.SelectedItems.Cast<ListViewItem>()
                            .Select(i => i.Tag).OfType<ScriptInfo>().ToList();
            if (toExport.Count == 0)
            {
                MessageBox.Show(this, "Bitte zuerst mindestens ein Skript in der Liste auswählen.",
                    "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
        }
        else
        {
            // Bewusst die gefilterte Liste: Wer nach „Heizung" sucht und dann exportiert,
            // meint die Treffer — nicht den gesamten Bestand. Der Knopf sagt das auch.
            toExport = _filtered;
            if (toExport.Count == 0)
            {
                MessageBox.Show(this, "Die aktuelle Filterung enthält kein Skript.",
                    "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
        }

        using var dlg = new FolderBrowserDialog
        {
            Description = $"Zielordner für {toExport.Count} Skript(e) wählen",
            UseDescriptionForTitle = true
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            Cursor = Cursors.WaitCursor;
            var result = ScriptExporter.Export(toExport, dlg.SelectedPath,
                _withGeneratedJs.Checked
                    ? ScriptExportFormat.WithGeneratedJs
                    : ScriptExportFormat.OriginalOnly,
                _data.SourceFile);
            Cursor = Cursors.Default;

            var msg = ScriptsPresenter.ExportSummary(result).Replace("\n", "\r\n");

            MessageBox.Show(this, msg, "Export abgeschlossen",
                MessageBoxButtons.OK,
                result.Errors.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);

            // Den angelegten Überordner öffnen, nicht den gewählten Zielordner.
            Process.Start(new ProcessStartInfo(result.RootDir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Cursor = Cursors.Default;
            Program.LogError("Export", ex);
            MessageBox.Show(this, "Der Export ist fehlgeschlagen:\r\n\r\n" + ex.Message
                + "\r\n\r\nDetails in:\r\n" + Program.ErrorLogPath,
                "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Exportiert die aktuell gefilterte Skriptliste als CSV (nicht die Dateien selbst).</summary>
    private void ExportListCsv()
    {
        CsvExport.Save(this, "skriptliste.csv",
            ScriptsPresenter.Columns,
            _filtered.Select(ScriptsPresenter.Row));
    }

    /// <summary>Kurze Rückmeldung in der Zählzeile; wird beim nächsten Filtern überschrieben.</summary>
    private void ShowToast(string message) => _count.Text = message;
}

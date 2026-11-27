using IobBackupAnalyzer.Core;

namespace IobBackupAnalyzer.App;

/// <summary>
/// Objekte aus dem Backup als Datei für „Objekte hochladen" im Admin exportieren — etwa
/// versehentlich gelöschte echarts-Vorlagen. Suche nach ID-Muster und Typ, Haken pro Zeile;
/// die Objekte selbst werden erst beim Export aus dem Archiv gelesen.
/// </summary>
public sealed class ObjectsTab : UserControl
{
    private readonly TextBox _pattern = new();
    private readonly ComboBox _type = new();
    private readonly CheckBox _includeChildren = new();
    private readonly CheckBox _allowGuarded = new();
    private readonly Label _count = new();
    private readonly Button _btnCheckAll = new();
    private readonly Button _btnUncheck = new();
    private readonly Button _btnExport = new();
    private readonly Button _btnClipboard = new();
    private readonly ListView _list = new();
    private readonly Label _placeholder = new();
    private readonly ToolTip _tips = new() { AutoPopDelay = 30000 };

    private BackupData? _data;
    private List<IobObject> _filtered = new();
    private readonly HashSet<string> _checked = new(StringComparer.Ordinal);
    private bool _filling;

    public ObjectsTab()
    {
        BuildUi();
    }

    private void BuildUi()
    {
        Padding = new Padding(8);

        var head = TabLayout.TopBar(72);

        var lbl = new Label { Text = "ID-Muster:", Location = new Point(0, 9), Size = new Size(72, 20) };
        _pattern.Location = new Point(74, 6);
        _pattern.Size = new Size(300, 24);
        _pattern.PlaceholderText = "z. B. echarts.0.*";
        _pattern.TextChanged += (_, _) => ApplyFilter();

        var lblType = new Label { Text = "Typ:", Location = new Point(388, 9), Size = new Size(32, 20) };
        _type.Location = new Point(422, 6);
        _type.Size = new Size(150, 24);
        _type.DropDownStyle = ComboBoxStyle.DropDownList;
        _type.SelectedIndexChanged += (_, _) => ApplyFilter();

        _includeChildren.Text = ObjectsPresenter.IncludeChildrenLabel;
        _includeChildren.Location = new Point(588, 8);
        _includeChildren.Size = new Size(200, 22);

        _allowGuarded.Text = ObjectsPresenter.AllowGuardedLabel;
        _allowGuarded.Location = new Point(794, 8);
        _allowGuarded.Size = new Size(200, 22);
        _allowGuarded.CheckedChanged += (_, _) => OnAllowGuardedChanged();

        _count.Location = new Point(0, 44);
        _count.Size = new Size(420, 20);
        _count.ForeColor = SystemColors.GrayText;

        _btnCheckAll.Text = "Alle Treffer anhaken";
        _btnCheckAll.Size = new Size(160, 26);
        _btnCheckAll.Location = new Point(0, 0);
        _btnCheckAll.Click += (_, _) => CheckAll(true);

        _btnUncheck.Text = "Haken entfernen";
        _btnUncheck.Size = new Size(130, 26);
        _btnUncheck.Location = new Point(168, 0);
        _btnUncheck.Click += (_, _) => CheckAll(false);

        _btnClipboard.Text = "In Zwischenablage";
        _btnClipboard.Size = new Size(150, 26);
        _btnClipboard.Location = new Point(306, 0);
        _btnClipboard.Click += (_, _) => Export(toClipboard: true);

        _btnExport.Text = "Als Import-JSON speichern …";
        _btnExport.Size = new Size(200, 26);
        _btnExport.Location = new Point(464, 0);
        _btnExport.Click += (_, _) => Export(toClipboard: false);

        var buttonBar = new Panel
        {
            Location = TabLayout.RightAligned(664, 40),
            Size = new Size(664, 30),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        buttonBar.Controls.AddRange(new Control[] { _btnCheckAll, _btnUncheck, _btnClipboard, _btnExport });

        head.Controls.AddRange(new Control[]
        {
            lbl, _pattern, lblType, _type, _includeChildren, _allowGuarded, _count, buttonBar
        });

        _tips.SetToolTip(_pattern, ObjectsPresenter.PatternHint);
        _tips.SetToolTip(_includeChildren, ObjectsPresenter.IncludeChildrenHint);
        _tips.SetToolTip(_allowGuarded, ObjectsPresenter.AllowGuardedHint);

        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.CheckBoxes = true;
        _list.FullRowSelect = true;
        _list.GridLines = true;
        _list.HideSelection = false;
        _list.Columns.Add("ID", 520);
        _list.Columns.Add("Typ", 110);
        _list.Columns.Add("Name", 300);
        _list.Columns.Add("Zurückgehalten", 280);
        _list.ItemChecked += OnItemChecked;

        ListViewCopy.Attach(_list);

        _placeholder.Dock = DockStyle.Fill;
        _placeholder.TextAlign = ContentAlignment.MiddleCenter;
        _placeholder.ForeColor = SystemColors.GrayText;
        _placeholder.Text = "Kein Backup geladen.\r\n\r\nBitte oben eine Datei öffnen oder hineinziehen.";

        Controls.Add(_list);
        Controls.Add(_placeholder);
        Controls.Add(head);
    }

    public void SetData(BackupData data)
    {
        _data = data;
        _checked.Clear();
        _placeholder.Visible = false;

        _type.Items.Clear();
        _type.Items.AddRange(ObjectsPresenter.TypeLabels(data).Cast<object>().ToArray());
        _type.SelectedIndex = 0;

        ApplyFilter();
    }

    /// <summary>
    /// Nur für den Bildmodus (<c>--screenshots</c>): setzt ein Suchmuster und hakt alle
    /// Treffer an, die nicht zurückgehalten werden. So zeigt das Bild den eigentlichen
    /// Ablauf — Muster, Haken, ausgegraute Sperren — statt einer ungefilterten Liste.
    /// </summary>
    internal void PresetForScreenshots(string pattern)
    {
        _pattern.Text = pattern;
        foreach (var o in _filtered)
            if (ObjectExportGuard.Reason(o) is null) _checked.Add(o.Id);
        FillList();
        UpdateCount();
    }

    private void ApplyFilter()
    {
        if (_data is null) return;

        _filtered = ObjectsPresenter.Filter(_data.Objects, _pattern.Text, _type.SelectedItem as string);
        FillList();
        UpdateCount();
    }

    private void FillList()
    {
        _filling = true;
        _list.BeginUpdate();
        _list.Items.Clear();

        foreach (var o in _filtered)
        {
            var item = new ListViewItem(ObjectsPresenter.Row(o))
            {
                Tag = o,
                Checked = _checked.Contains(o.Id),
                ForeColor = ObjectsPresenter.Emphasis(o) == RowEmphasis.Muted
                    ? SystemColors.GrayText : SystemColors.ControlText
            };
            _list.Items.Add(item);
        }

        _list.EndUpdate();
        _filling = false;
    }

    private void OnItemChecked(object? sender, ItemCheckedEventArgs e)
    {
        if (_filling || e.Item.Tag is not IobObject o) return;
        if (e.Item.Checked) _checked.Add(o.Id); else _checked.Remove(o.Id);
        UpdateCount();
    }

    private void CheckAll(bool check)
    {
        if (check) foreach (var o in _filtered) _checked.Add(o.Id);
        else _checked.Clear();
        FillList();
        UpdateCount();
    }

    private void UpdateCount()
    {
        if (_data is null) return;
        _count.Text = ObjectsPresenter.CountText(_filtered.Count, _data.Objects.Count, _checked.Count);
    }

    private void OnAllowGuardedChanged()
    {
        if (!_allowGuarded.Checked) return;

        var ok = MessageBox.Show(this, ObjectsPresenter.AllowGuardedWarning.Replace("\n", "\r\n"),
            "Zurückgehaltene zulassen", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (ok != DialogResult.Yes) _allowGuarded.Checked = false;
    }

    private void Export(bool toClipboard)
    {
        if (_data is null) return;

        var sel = ObjectsPresenter.Resolve(_data, _checked, _includeChildren.Checked, _allowGuarded.Checked);
        ExportSelection(this, _data, sel, ObjectExporter.FileName(_data, _pattern.Text), toClipboard);
    }

    /// <summary>
    /// Der gemeinsame Ablauf für diesen Tab und den Vergleich: Bestätigung mit den Hinweisen,
    /// Datei lesen, speichern oder kopieren.
    /// </summary>
    internal static void ExportSelection(IWin32Window owner, BackupData data, ObjectsPresenter.Selection sel,
                                         string fileName, bool toClipboard)
    {
        if (sel.Export.Count == 0)
        {
            MessageBox.Show(owner,
                sel.Guarded.Count > 0
                    ? "Alle gewählten Objekte werden zurückgehalten (Zugangsdaten oder Adapter/Instanz).\r\n\r\n"
                      + "Wer sie trotzdem braucht, schaltet „Zurückgehaltene zulassen\" ein."
                    : "Bitte zuerst mindestens ein Objekt anhaken.",
                "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (MessageBox.Show(owner, ObjectsPresenter.ConfirmText(sel).Replace("\n", "\r\n"),
                toClipboard ? "In die Zwischenablage kopieren" : "Als Import-JSON speichern",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
            return;

        string? target = null;
        if (!toClipboard)
        {
            using var dlg = new SaveFileDialog
            {
                Title = "Import-JSON speichern",
                FileName = fileName,
                Filter = "JSON (*.json)|*.json|Alle Dateien (*.*)|*.*",
                OverwritePrompt = true
            };
            if (dlg.ShowDialog(owner) != DialogResult.OK) return;
            target = dlg.FileName;

            if (ExportPaths.ArchiveWarning(target) is { } warning
                && MessageBox.Show(owner, warning.Replace("\n", "\r\n"), "Sicher?", MessageBoxButtons.YesNo,
                       MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
        }

        try
        {
            Cursor.Current = Cursors.WaitCursor;
            var result = ObjectExporter.Build(data, sel.Export);

            if (target is null)
            {
                Clipboard.SetText(result.Json);
                Cursor.Current = Cursors.Default;
                MessageBox.Show(owner, ObjectsPresenter.DoneText(result, "in die Zwischenablage kopiert"),
                    "Fertig", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                ObjectExporter.WriteFile(result.Json, target, data.SourceFile);
                Cursor.Current = Cursors.Default;
                MessageBox.Show(owner, ObjectsPresenter.DoneText(result, "gespeichert in:\r\n" + target),
                    "Fertig", MessageBoxButtons.OK,
                    result.Missing.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            Cursor.Current = Cursors.Default;
            Program.LogError("Objekt-Export", ex);
            MessageBox.Show(owner, "Der Export ist fehlgeschlagen:\r\n\r\n" + ex.Message
                + "\r\n\r\nDetails in:\r\n" + Program.ErrorLogPath,
                "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

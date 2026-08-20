using IobBackupAnalyzer.Core;

namespace IobBackupAnalyzer.App;

/// <summary>
/// Vergleich zweier Backups: was hat sich zwischen zwei Ständen geändert?
///
/// Das erste Backup ist das im Hauptfenster geladene, das zweite wird hier ausgewählt
/// oder hineingezogen. Welches der beiden „vorher" ist, entscheidet der Backup-Zeitpunkt,
/// nicht die Reihenfolge des Ladens.
/// </summary>
public sealed class CompareTab : UserControl
{
    private readonly Label _lblLoaded = new();
    private readonly Button _btnPick = new();
    private readonly Label _lblDrop = new();
    private readonly Button _btnSwap = new();
    private readonly Label _summary = new();
    private readonly Label _systemInfo = new();
    private readonly ProgressBar _progress = new();

    private readonly TabControl _sub = new();
    private readonly Label _placeholder = new();

    private readonly ListView _metrics = new();
    private readonly ListView _instances = new();
    private readonly ListView _scripts = new();
    private readonly RichTextBox _diff = new();
    private readonly CheckBox _onlyChangedLines = new();
    private readonly Label _diffInfo = new();
    private readonly ListView _namespaces = new();
    private readonly ListView _objectIds = new();
    private readonly ListView _views = new();

    private readonly CheckBox _hideUnchangedInstances = new();
    private readonly CheckBox _hideUnchangedScripts = new();
    private readonly CheckBox _hideUnchangedViews = new();

    private BackupData? _loaded;
    private BackupData? _other;
    private BackupComparison? _cmp;
    private CancellationTokenSource? _cts;

    public CompareTab()
    {
        BuildUi();
    }

    // ------------------------------------------------------------------- Aufbau

    private void BuildUi()
    {
        Padding = new Padding(8);

        var head = TabLayout.TopBar(128);

        _lblLoaded.Location = new Point(0, 0);
        _lblLoaded.Size = new Size(1100, 20);
        _lblLoaded.AutoEllipsis = true;
        _lblLoaded.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _lblLoaded.Text = "Kein Backup geladen.";

        _btnPick.Text = "Zweites Backup wählen …";
        _btnPick.Size = new Size(180, 30);
        _btnPick.Location = new Point(0, 26);
        _btnPick.Click += async (_, _) => await PickAsync();

        _lblDrop.Text = "… oder zweites Backup hier hineinziehen";
        _lblDrop.TextAlign = ContentAlignment.MiddleCenter;
        _lblDrop.BorderStyle = BorderStyle.FixedSingle;
        _lblDrop.ForeColor = SystemColors.GrayText;
        _lblDrop.Location = new Point(190, 26);
        _lblDrop.Size = new Size(300, 30);
        _lblDrop.AllowDrop = true;
        _lblDrop.DragEnter += OnDragEnter;
        _lblDrop.DragDrop += OnDragDrop;

        _btnSwap.Text = "Zweites Backup entfernen";
        _btnSwap.Size = new Size(180, 30);
        _btnSwap.Location = new Point(500, 26);
        _btnSwap.Enabled = false;
        _btnSwap.Click += (_, _) => Reset();

        _progress.Location = new Point(690, 26);
        _progress.Size = new Size(160, 30);
        _progress.Style = ProgressBarStyle.Marquee;
        _progress.Visible = false;

        _summary.Location = new Point(0, 62);
        _summary.Size = new Size(1100, 38);
        _summary.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _summary.ForeColor = SystemColors.GrayText;

        // Herkunft der beiden Backups — eigene Zeile, weil sie im Zweifel wichtiger ist
        // als jedes Vergleichsergebnis darunter.
        _systemInfo.Location = new Point(0, 102);
        _systemInfo.Size = new Size(1100, 22);
        _systemInfo.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _systemInfo.AutoEllipsis = true;
        _systemInfo.TextAlign = ContentAlignment.MiddleLeft;

        head.Controls.AddRange(new Control[]
        {
            _lblLoaded, _btnPick, _lblDrop, _btnSwap, _progress, _summary, _systemInfo
        });

        _sub.Dock = DockStyle.Fill;
        _sub.Padding = new Point(10, 4);
        _sub.Visible = false;
        _sub.TabPages.Add(BuildMetricsPage());
        _sub.TabPages.Add(BuildInstancesPage());
        _sub.TabPages.Add(BuildScriptsPage());
        _sub.TabPages.Add(BuildObjectsPage());
        _sub.TabPages.Add(BuildViewsPage());

        _placeholder.Dock = DockStyle.Fill;
        _placeholder.TextAlign = ContentAlignment.MiddleCenter;
        _placeholder.ForeColor = SystemColors.GrayText;
        _placeholder.Text = "Kein Backup geladen.";

        Controls.Add(_sub);
        Controls.Add(_placeholder);
        Controls.Add(head);

        // Auch auf der ganzen Fläche darf abgelegt werden, nicht nur auf dem Feld.
        AllowDrop = true;
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
    }

    private TabPage BuildMetricsPage()
    {
        var page = new TabPage("Kennzahlen") { Padding = new Padding(8), UseVisualStyleBackColor = true };

        var bar = TabLayout.TopBar(36);
        var csv = new Button { Text = "Als CSV exportieren", Size = new Size(160, 26), Location = new Point(0, 5) };
        csv.Click += (_, _) => ExportMetrics();
        bar.Controls.Add(csv);

        _metrics.Dock = DockStyle.Fill;
        _metrics.View = View.Details;
        _metrics.FullRowSelect = true;
        _metrics.GridLines = true;
        _metrics.HideSelection = false;
        _metrics.MultiSelect = true;
        _metrics.Columns.Add("Kennzahl", 240);
        _metrics.Columns.Add("Vorher", 120, HorizontalAlignment.Right);
        _metrics.Columns.Add("Nachher", 120, HorizontalAlignment.Right);
        _metrics.Columns.Add("Differenz", 120, HorizontalAlignment.Right);
        ListViewCopy.Attach(_metrics);
        ListViewSort.Attach(_metrics);

        page.Controls.Add(_metrics);
        page.Controls.Add(bar);
        return page;
    }

    private TabPage BuildInstancesPage()
    {
        var page = new TabPage("Adapter-Instanzen") { Padding = new Padding(8), UseVisualStyleBackColor = true };

        var bar = TabLayout.TopBar(36);

        _hideUnchangedInstances.Text = "Nur Änderungen anzeigen";
        _hideUnchangedInstances.Checked = true;
        _hideUnchangedInstances.Location = new Point(0, 8);
        _hideUnchangedInstances.Size = new Size(200, 22);
        _hideUnchangedInstances.CheckedChanged += (_, _) => FillInstances();

        var csv = new Button { Text = "Als CSV exportieren", Size = new Size(160, 26), Location = new Point(220, 5) };
        csv.Click += (_, _) => ExportInstances();

        bar.Controls.AddRange(new Control[] { _hideUnchangedInstances, csv });

        _instances.Dock = DockStyle.Fill;
        _instances.View = View.Details;
        _instances.FullRowSelect = true;
        _instances.GridLines = true;
        _instances.HideSelection = false;
        _instances.MultiSelect = true;
        _instances.Columns.Add("Instanz", 220);
        _instances.Columns.Add("Änderung", 100);
        _instances.Columns.Add("Version", 200);
        _instances.Columns.Add("Aktiviert", 140);
        _instances.Columns.Add("Details", 320);
        ListViewCopy.Attach(_instances);
        ListViewSort.Attach(_instances);

        page.Controls.Add(_instances);
        page.Controls.Add(bar);
        return page;
    }

    private TabPage BuildScriptsPage()
    {
        var page = new TabPage("Skripte") { Padding = new Padding(8), UseVisualStyleBackColor = true };

        var bar = TabLayout.TopBar(36);

        _hideUnchangedScripts.Text = "Nur Änderungen anzeigen";
        _hideUnchangedScripts.Checked = true;
        _hideUnchangedScripts.Location = new Point(0, 8);
        _hideUnchangedScripts.Size = new Size(200, 22);
        _hideUnchangedScripts.CheckedChanged += (_, _) => FillScripts();

        _onlyChangedLines.Text = "Im Vergleich nur geänderte Stellen zeigen";
        _onlyChangedLines.Checked = true;
        _onlyChangedLines.Location = new Point(220, 8);
        _onlyChangedLines.Size = new Size(290, 22);
        _onlyChangedLines.CheckedChanged += (_, _) => ShowDiffForSelection();

        var csv = new Button { Text = "Als CSV exportieren", Size = new Size(160, 26), Location = new Point(520, 5) };
        csv.Click += (_, _) => ExportScripts();

        _diffInfo.Location = new Point(690, 11);
        _diffInfo.Size = new Size(420, 20);
        _diffInfo.ForeColor = SystemColors.GrayText;

        bar.Controls.AddRange(new Control[] { _hideUnchangedScripts, _onlyChangedLines, csv, _diffInfo });

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        SetSplitterWhenSized(split, 240);

        _scripts.Dock = DockStyle.Fill;
        _scripts.View = View.Details;
        _scripts.FullRowSelect = true;
        _scripts.GridLines = true;
        _scripts.HideSelection = false;
        _scripts.MultiSelect = true;
        _scripts.Columns.Add("Skript", 340);
        _scripts.Columns.Add("Typ", 120);
        _scripts.Columns.Add("Änderung", 100);
        _scripts.Columns.Add("Details", 240);
        _scripts.SelectedIndexChanged += (_, _) => ShowDiffForSelection();
        ListViewCopy.Attach(_scripts);
        ListViewSort.Attach(_scripts);

        _diff.Dock = DockStyle.Fill;
        _diff.ReadOnly = true;
        _diff.WordWrap = false;
        _diff.Font = new Font("Consolas", 9F);
        _diff.BackColor = Color.White;
        _diff.DetectUrls = false;

        split.Panel1.Controls.Add(_scripts);
        split.Panel2.Controls.Add(_diff);

        page.Controls.Add(split);
        page.Controls.Add(bar);
        return page;
    }

    private TabPage BuildObjectsPage()
    {
        var page = new TabPage("Objekte") { Padding = new Padding(8), UseVisualStyleBackColor = true };

        var bar = TabLayout.TopBar(36);
        var hint = new Label
        {
            Text = "Namensraum auswählen, um die betroffenen Objekt-IDs zu sehen.",
            Location = new Point(0, 10),
            Size = new Size(420, 20),
            ForeColor = SystemColors.GrayText
        };
        var csv = new Button { Text = "Als CSV exportieren", Size = new Size(160, 26), Location = new Point(430, 5) };
        csv.Click += (_, _) => ExportObjects();
        bar.Controls.AddRange(new Control[] { hint, csv });

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
        SetSplitterWhenSized(split, 420);

        _namespaces.Dock = DockStyle.Fill;
        _namespaces.View = View.Details;
        _namespaces.FullRowSelect = true;
        _namespaces.GridLines = true;
        _namespaces.HideSelection = false;
        _namespaces.Columns.Add("Namensraum", 200);
        _namespaces.Columns.Add("Neu", 70, HorizontalAlignment.Right);
        _namespaces.Columns.Add("Entfernt", 80, HorizontalAlignment.Right);
        _namespaces.Columns.Add("Saldo", 70, HorizontalAlignment.Right);
        _namespaces.SelectedIndexChanged += (_, _) => FillObjectIds();
        ListViewCopy.Attach(_namespaces);
        ListViewSort.Attach(_namespaces);

        _objectIds.Dock = DockStyle.Fill;
        _objectIds.View = View.Details;
        _objectIds.FullRowSelect = true;
        _objectIds.GridLines = true;
        _objectIds.HideSelection = false;
        _objectIds.MultiSelect = true;
        _objectIds.Columns.Add("Änderung", 90);
        _objectIds.Columns.Add("Objekt-ID", 600);
        ListViewCopy.Attach(_objectIds);
        ListViewSort.Attach(_objectIds);

        split.Panel1.Controls.Add(_namespaces);
        split.Panel2.Controls.Add(_objectIds);

        page.Controls.Add(split);
        page.Controls.Add(bar);
        return page;
    }

    private TabPage BuildViewsPage()
    {
        var page = new TabPage("VIS-Views") { Padding = new Padding(8), UseVisualStyleBackColor = true };

        var bar = TabLayout.TopBar(36);

        _hideUnchangedViews.Text = "Nur Änderungen anzeigen";
        _hideUnchangedViews.Checked = true;
        _hideUnchangedViews.Location = new Point(0, 8);
        _hideUnchangedViews.Size = new Size(200, 22);
        _hideUnchangedViews.CheckedChanged += (_, _) => FillViews();

        var csv = new Button { Text = "Als CSV exportieren", Size = new Size(160, 26), Location = new Point(220, 5) };
        csv.Click += (_, _) => ExportViews();

        bar.Controls.AddRange(new Control[] { _hideUnchangedViews, csv });

        _views.Dock = DockStyle.Fill;
        _views.View = View.Details;
        _views.FullRowSelect = true;
        _views.GridLines = true;
        _views.HideSelection = false;
        _views.MultiSelect = true;
        _views.Columns.Add("VIS", 70);
        _views.Columns.Add("View", 260);
        _views.Columns.Add("Änderung", 100);
        _views.Columns.Add("Widgets", 120, HorizontalAlignment.Right);
        _views.Columns.Add("Details", 340);
        ListViewCopy.Attach(_views);
        ListViewSort.Attach(_views);

        page.Controls.Add(_views);
        page.Controls.Add(bar);
        return page;
    }

    /// <summary>
    /// Setzt die Teilerposition erst, wenn der SplitContainer seine endgültige Größe hat.
    /// Direkt im Konstruktor gesetzt, wäre der Wert größer als das noch ungelayoutete
    /// Control — WinForms lehnt das ab.
    /// </summary>
    private static void SetSplitterWhenSized(SplitContainer split, int distance)
    {
        void Apply(object? sender, EventArgs e)
        {
            var span = split.Orientation == Orientation.Horizontal ? split.Height : split.Width;
            var max = span - split.Panel2MinSize - split.SplitterWidth;
            if (max <= split.Panel1MinSize) return;   // noch zu klein, später erneut versuchen

            split.SplitterDistance = Math.Clamp(distance, split.Panel1MinSize, max);
            split.SizeChanged -= Apply;               // nur die erste sinnvolle Größe zählt
        }

        split.SizeChanged += Apply;
    }

    // -------------------------------------------------------------------- Laden

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (_loaded is null) return;
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
        {
            e.Effect = DragDropEffects.Copy;
            _lblDrop.BackColor = SystemColors.Highlight;
            _lblDrop.ForeColor = SystemColors.HighlightText;
        }
    }

    private async void OnDragDrop(object? sender, DragEventArgs e)
    {
        _lblDrop.BackColor = SystemColors.Control;
        _lblDrop.ForeColor = SystemColors.GrayText;

        if (_loaded is null) return;
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            await LoadOtherAsync(files[0]);
    }

    private async Task PickAsync()
    {
        if (_loaded is null) return;

        using var dlg = new OpenFileDialog
        {
            Title = "Zweites Backup zum Vergleich auswählen",
            Filter = "ioBroker-Backups (*.tar.gz;*.json;*.jsonl)|*.tar.gz;*.json;*.jsonl|Alle Dateien (*.*)|*.*",
            CheckFileExists = true
        };

        var dir = Path.GetDirectoryName(_loaded.SourceFile);
        if (dir is not null && Directory.Exists(dir)) dlg.InitialDirectory = dir;

        if (dlg.ShowDialog(this) == DialogResult.OK)
            await LoadOtherAsync(dlg.FileName);
    }

    private async Task LoadOtherAsync(string path)
    {
        if (_loaded is null) return;

        if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(_loaded.SourceFile),
                          StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this,
                "Das ist dieselbe Datei, die bereits geladen ist.\r\n\r\n" +
                "Bitte ein anderes Backup zum Vergleich auswählen.",
                "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        SetBusy(true);
        try
        {
            var other = await BackupLoader.LoadAsync(path, null, _cts.Token);

            // Ein Skript-Backup gegen ein Voll-Backup zu stellen ergäbe lauter
            // Scheinänderungen — jedes Objekt und jede Instanz fehlte auf einer Seite.
            if (other.Kind != _loaded.Kind)
            {
                MessageBox.Show(this,
                    ComparePresenter.NotComparableText(_loaded.Kind, other.Kind).Replace("\n", "\r\n"),
                    "Nicht vergleichbar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Stammen beide vom selben ioBroker? Ein Unterschied ist kein Fehler, aber er
            // erklärt tausende Scheinänderungen — deshalb nachfragen statt stillschweigend
            // vergleichen.
            if (!ConfirmDifferentSystem(_loaded.System, other.System)) return;

            _other = other;
            _cmp = await BackupComparer.CompareAsync(_loaded, other, _cts.Token);

            FillAll();
            _sub.Visible = true;
            _placeholder.Visible = false;
            _btnSwap.Enabled = true;
        }
        catch (OperationCanceledException)
        {
            // Abbruch durch einen neuen Ladevorgang — nichts zu tun.
        }
        catch (NotABackupException ex)
        {
            MessageBox.Show(this, $"Datei: {Path.GetFileName(path)}\r\n\r\n{ex.Message}",
                "Backup konnte nicht geladen werden", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            Program.LogError($"Vergleich fehlgeschlagen: {path}", ex);
            MessageBox.Show(this,
                "Die Datei konnte nicht geladen werden:\r\n\r\n" + ex.Message +
                "\r\n\r\nDetails wurden protokolliert in:\r\n" + Program.ErrorLogPath,
                "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static string KindText(BackupKind kind) =>
        kind == BackupKind.Full ? "Voll-Backup" : "Skript-Backup";

    /// <summary>
    /// Fragt nach, wenn die beiden Backups aus verschiedenen ioBroker-Installationen
    /// stammen. Der Vergleich bleibt möglich — er ist bei einem Umzug auf neue Hardware
    /// oder beim Abgleich zweier Systeme genau das Gewollte —, aber er soll nie
    /// unbemerkt passieren: Zwei verschiedene Systeme erzeugen tausende Unterschiede, die wie
    /// dramatische Änderungen aussehen.
    ///
    /// Rückgabe false = Abbruch durch den Benutzer.
    /// </summary>
    private bool ConfirmDifferentSystem(SystemIdentity mine, SystemIdentity other)
    {
        if (BackupComparer.MatchSystems(mine, other) != SystemMatch.Different) return true;

        var answer = MessageBox.Show(this,
            ComparePresenter.DifferentSystemText(mine, other).Replace("\n", "\r\n"),
            "Verschiedene Systeme", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        return answer == DialogResult.Yes;
    }

    private void SetBusy(bool busy)
    {
        _progress.Visible = busy;
        _btnPick.Enabled = !busy;
        _sub.Enabled = !busy;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        Application.DoEvents();
    }

    private void Reset()
    {
        _other = null;
        _cmp = null;
        _sub.Visible = false;
        _placeholder.Visible = true;
        _btnSwap.Enabled = false;
        _summary.Text = "";
        _systemInfo.Text = "";
        UpdatePlaceholder();
    }

    // ------------------------------------------------------------------ Anzeige

    private void FillAll()
    {
        if (_cmp is null) return;

        var c = _cmp;

        _summary.Text = ComparePresenter.SummaryText(c).Replace("\n", "\r\n");
        _summary.ForeColor = c.IsIdentical ? SystemColors.GrayText : SystemColors.ControlText;

        _systemInfo.Text = c.SystemMatchText;
        _systemInfo.ForeColor = c.SystemMatch switch
        {
            SystemMatch.Same => Color.SeaGreen,
            SystemMatch.Different => Color.Firebrick,
            SystemMatch.Probable => SystemColors.ControlText,
            _ => SystemColors.GrayText
        };

        FillMetrics();
        FillInstances();
        FillScripts();
        FillNamespaces();
        FillViews();
    }

    private void FillMetrics()
    {
        _metrics.BeginUpdate();
        _metrics.Items.Clear();
        foreach (var m in _cmp!.Metrics)
        {
            var item = new ListViewItem(ComparePresenter.Row(m));

            item.SubItems[3].ForeColor = ComparePresenter.Emphasis(m) switch
            {
                RowEmphasis.Positive => Color.SeaGreen,
                RowEmphasis.Problem => Color.Firebrick,
                _ => SystemColors.ControlText
            };
            item.UseItemStyleForSubItems = false;
            _metrics.Items.Add(item);
        }
        _metrics.EndUpdate();
    }

    private void FillInstances()
    {
        if (_cmp is null) return;

        var rows = ComparePresenter.FilterInstances(_cmp, _hideUnchangedInstances.Checked);

        _instances.BeginUpdate();
        _instances.Items.Clear();
        foreach (var i in rows)
        {
            var item = new ListViewItem(ComparePresenter.DisplayRow(i)) { Tag = i };
            item.ForeColor = ColorOf(ComparePresenter.Emphasis(i));
            _instances.Items.Add(item);
        }
        _instances.EndUpdate();
    }

    private void FillScripts()
    {
        if (_cmp is null) return;

        var rows = ComparePresenter.FilterScripts(_cmp, _hideUnchangedScripts.Checked);

        _scripts.BeginUpdate();
        _scripts.Items.Clear();
        foreach (var s in rows)
        {
            var item = new ListViewItem(ComparePresenter.DisplayRow(s)) { Tag = s };
            item.ForeColor = ColorOf(ComparePresenter.Emphasis(s));
            _scripts.Items.Add(item);
        }
        _scripts.EndUpdate();

        _diff.Clear();
        _diffInfo.Text = rows.Count == 0
            ? "Keine Skriptänderungen."
            : "Skript auswählen, um den Vergleich zu sehen.";
    }

    /// <summary>
    /// Zeigt den Zeilenvergleich des ausgewählten Skripts. Bei Blockly wird das XML
    /// verglichen — es ist die eigentliche Quelle, das JavaScript daneben nur erzeugt.
    /// </summary>
    private void ShowDiffForSelection()
    {
        if (_scripts.SelectedItems.Count == 0 || _scripts.SelectedItems[0].Tag is not ScriptChange sc)
            return;

        var oldText = sc.Before is null ? "" : ScriptChange.ComparableText(sc.Before);
        var newText = sc.After is null ? "" : ScriptChange.ComparableText(sc.After);

        var basis = ComparePresenter.DiffBasis(sc);

        if (sc.OnlyStatusChanged)
        {
            _diff.Clear();
            _diff.Text = "Der Inhalt ist unverändert — geändert hat sich nur der Aktiv-Status.";
            _diffInfo.Text = basis;
            return;
        }

        Cursor = Cursors.WaitCursor;
        try
        {
            var result = TextDiff.Compare(oldText, newText);
            RenderDiff(result);

            _diffInfo.Text = ComparePresenter.DiffInfoText(sc, result);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    /// <summary>
    /// Zeichnet die vom Presenter ausgewählten Zeilen. Welche Zeilen das sind (nur
    /// Änderungen samt Umfeld, Lückenmarkierung, Obergrenze) entscheidet
    /// <see cref="ComparePresenter.VisibleLines"/> — dieselbe Auswahl wie in der
    /// Avalonia-Fassung.
    /// </summary>
    private void RenderDiff(DiffResult result)
    {
        var lines = ComparePresenter.VisibleLines(result, _onlyChangedLines.Checked);

        _diff.SuspendLayout();
        _diff.Clear();

        foreach (var l in lines)
        {
            if (l.IsGap)
            {
                Append("   " + l.Text + "\n", SystemColors.GrayText, Color.White);
                continue;
            }

            var (fore, back) = l.Kind switch
            {
                DiffKind.Added => (Color.FromArgb(0, 90, 0), Color.FromArgb(226, 248, 226)),
                DiffKind.Removed => (Color.FromArgb(130, 0, 0), Color.FromArgb(255, 231, 231)),
                _ => (SystemColors.ControlText, Color.White)
            };

            Append($"{l.Old,5} {l.New,5} {l.Marker} {l.Text}\n", fore, back);
        }

        if (lines.Count == 0)
            Append("Keine inhaltlichen Unterschiede.", SystemColors.GrayText, Color.White);

        _diff.ResumeLayout();
        _diff.SelectionStart = 0;
        _diff.ScrollToCaret();
    }

    /// <summary>Übersetzt die Einstufung des Presenters in die Farben der Windows-Fassung.</summary>
    private static Color ColorOf(RowEmphasis emphasis) => emphasis switch
    {
        RowEmphasis.Positive => Color.SeaGreen,
        RowEmphasis.Problem => Color.Firebrick,
        RowEmphasis.Warn => Color.DarkOrange,
        RowEmphasis.Muted => SystemColors.GrayText,
        _ => SystemColors.ControlText
    };

    private void Append(string text, Color fore, Color back)
    {
        _diff.SelectionStart = _diff.TextLength;
        _diff.SelectionLength = 0;
        _diff.SelectionColor = fore;
        _diff.SelectionBackColor = back;
        _diff.AppendText(text);
    }

    private void FillNamespaces()
    {
        if (_cmp is null) return;

        _namespaces.BeginUpdate();
        _namespaces.Items.Clear();
        foreach (var n in _cmp.Namespaces)
        {
            var item = new ListViewItem(ComparePresenter.Row(n)) { Tag = n };
            item.ForeColor = ColorOf(ComparePresenter.Emphasis(n));
            _namespaces.Items.Add(item);
        }
        _namespaces.EndUpdate();

        _objectIds.Items.Clear();
        if (_namespaces.Items.Count > 0) _namespaces.Items[0].Selected = true;
    }

    private void FillObjectIds()
    {
        if (_namespaces.SelectedItems.Count == 0 || _namespaces.SelectedItems[0].Tag is not NamespaceChange n)
            return;

        _objectIds.BeginUpdate();
        _objectIds.Items.Clear();

        foreach (var (change, id, isAdded) in ComparePresenter.ObjectIds(n))
            _objectIds.Items.Add(new ListViewItem(new[] { change, id })
            {
                ForeColor = isAdded ? Color.SeaGreen : Color.Firebrick
            });

        _objectIds.EndUpdate();
    }

    private void FillViews()
    {
        if (_cmp is null) return;

        var rows = ComparePresenter.FilterViews(_cmp, _hideUnchangedViews.Checked);

        _views.BeginUpdate();
        _views.Items.Clear();
        foreach (var v in rows)
        {
            var item = new ListViewItem(ComparePresenter.DisplayRow(v));
            item.ForeColor = ColorOf(ComparePresenter.Emphasis(v));
            _views.Items.Add(item);
        }
        _views.EndUpdate();

        if (_cmp.Views.Count == 0)
            _views.Items.Add(new ListViewItem(new[]
            {
                "", "Keine VIS-Views in beiden Backups vorhanden.", "", "", ""
            })
            { ForeColor = SystemColors.GrayText });
    }

    // ------------------------------------------------------------------- Export

    private void ExportMetrics()
    {
        if (_cmp is null) return;
        CsvExport.Save(this, "vergleich-kennzahlen.csv",
            ComparePresenter.MetricColumns,
            _cmp.Metrics.Select(ComparePresenter.Row));
    }

    private void ExportInstances()
    {
        if (_cmp is null) return;
        CsvExport.Save(this, "vergleich-instanzen.csv",
            ComparePresenter.InstanceCsvColumns,
            ComparePresenter.FilterInstances(_cmp, _hideUnchangedInstances.Checked)
                            .Select(ComparePresenter.Row));
    }

    private void ExportScripts()
    {
        if (_cmp is null) return;
        CsvExport.Save(this, "vergleich-skripte.csv",
            ComparePresenter.ScriptCsvColumns,
            ComparePresenter.FilterScripts(_cmp, _hideUnchangedScripts.Checked)
                            .Select(ComparePresenter.Row));
    }

    private void ExportObjects()
    {
        if (_cmp is null) return;

        CsvExport.Save(this, "vergleich-objekte.csv",
            ComparePresenter.ObjectCsvColumns,
            ComparePresenter.ObjectCsvRows(_cmp));
    }

    private void ExportViews()
    {
        if (_cmp is null) return;
        CsvExport.Save(this, "vergleich-views.csv",
            ComparePresenter.ViewCsvColumns,
            ComparePresenter.FilterViews(_cmp, _hideUnchangedViews.Checked)
                            .Select(ComparePresenter.Row));
    }

    // ------------------------------------------------------------------ Zustand

    public void SetData(BackupData data)
    {
        _loaded = data;

        // Ein neu geladenes Hauptbackup macht den bisherigen Vergleich ungültig.
        Reset();

        _lblLoaded.Text = ComparePresenter.LoadedText(data);
        _btnPick.Enabled = true;
    }

    public void SetAvailable(bool available)
    {
        _btnPick.Enabled = available && _loaded is not null;
        if (!available)
        {
            _sub.Visible = false;
            _placeholder.Visible = true;
        }
        UpdatePlaceholder();
    }

    private void UpdatePlaceholder()
    {
        _placeholder.Text = ComparePresenter.PlaceholderText(_loaded).Replace("\n", "\r\n");
        _placeholder.BringToFront();
    }
}

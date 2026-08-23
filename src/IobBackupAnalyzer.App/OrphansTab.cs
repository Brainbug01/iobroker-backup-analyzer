using System.Diagnostics;
using IobBackupAnalyzer.Core;

namespace IobBackupAnalyzer.App;

/// <summary>Säule 3: Verwaiste Datenpunkte — Analyse A und B in je einem Untertab.</summary>
public sealed class OrphansTab : UserControl
{
    private readonly TabControl _sub = new();
    private readonly Label _placeholder = new();

    // Analyse A
    private readonly ListView _listA = new();
    private readonly Label _countA = new();
    private readonly TextBox _filterA = new();
    private readonly Button _csvA = new();

    // Analyse B
    private readonly ListView _listB = new();
    private readonly Label _countB = new();
    private readonly TextBox _filterB = new();
    private readonly CheckBox _showAllB = new();
    private readonly Button _csvB = new();

    // Analyse C — States
    private readonly ComboBox _viewC = new();
    private readonly TextBox _filterC = new();
    private readonly Label _countC = new();
    private readonly Label _statsC = new();
    private readonly ListView _listC = new();
    private readonly Button _csvC = new();
    private readonly Button _cleanupC = new();

    private BackupData? _data;
    private List<OrphanObject> _orphans = new();
    private List<UnusedDatapoint> _unused = new();
    private StateReport? _states;

    public OrphansTab()
    {
        BuildUi();
    }

    private void BuildUi()
    {
        Padding = new Padding(8);

        _sub.Dock = DockStyle.Fill;
        _sub.Padding = new Point(10, 4);
        _sub.TabPages.Add(BuildAnalysisA());
        _sub.TabPages.Add(BuildAnalysisB());
        _sub.TabPages.Add(BuildAnalysisC());

        _placeholder.Dock = DockStyle.Fill;
        _placeholder.TextAlign = ContentAlignment.MiddleCenter;
        _placeholder.ForeColor = SystemColors.GrayText;
        _placeholder.Text = "Kein Backup geladen.";

        Controls.Add(_sub);
        Controls.Add(_placeholder);
    }

    /// <summary>Der Warnhinweis — dauerhaft sichtbar, nicht wegklickbar.</summary>
    private static Label BuildWarning(string text)
    {
        return new Label
        {
            Dock = DockStyle.Top,
            Height = 46,
            Text = text,
            BackColor = Color.FromArgb(255, 249, 219),
            ForeColor = Color.FromArgb(90, 60, 0),
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(8, 4, 8, 4),
            TextAlign = ContentAlignment.MiddleLeft
        };
    }

    // ---------------------------------------------------------------- Analyse A

    private TabPage BuildAnalysisA()
    {
        var page = new TabPage("A — Objekt-Leichen") { Padding = new Padding(8), UseVisualStyleBackColor = true };

        var warn = BuildWarning(OrphansPresenter.WarningA);

        var bar = TabLayout.TopBar(40);

        var lbl = new Label { Text = "Filter:", Location = new Point(0, 11), Size = new Size(44, 20) };
        _filterA.Location = new Point(46, 8);
        _filterA.Size = new Size(300, 24);
        _filterA.PlaceholderText = "Objekt-ID oder Instanz …";
        _filterA.TextChanged += (_, _) => FillA();

        _countA.Location = new Point(360, 11);
        _countA.Size = new Size(400, 20);
        _countA.ForeColor = SystemColors.GrayText;

        _csvA.Text = "Als CSV exportieren";
        _csvA.Size = new Size(160, 26);
        _csvA.Location = new Point(770, 7);
        _csvA.Click += (_, _) => ExportA();

        bar.Controls.AddRange(new Control[] { lbl, _filterA, _countA, _csvA });

        // Rechtsbündigen Button zuverlässig positionieren (Anchor greift im verschachtelten
        // TabControl nicht verlässlich).
        bar.SizeChanged += (_, _) =>
        {
            _csvA.Left = bar.ClientSize.Width - _csvA.Width - 4;
            _countA.Width = Math.Max(60, _csvA.Left - _countA.Location.X - 8);
        };

        _listA.Dock = DockStyle.Fill;
        _listA.View = View.Details;
        _listA.FullRowSelect = true;
        _listA.GridLines = true;
        _listA.Columns.Add("Fehlende Instanz", 200);
        _listA.Columns.Add("Objekt-ID", 460);
        _listA.Columns.Add("Typ", 100);
        _listA.Columns.Add("Name", 240);
        _listA.MultiSelect = true;
        _listA.HideSelection = false;
        ListViewCopy.Attach(_listA);
        ListViewSort.Attach(_listA);

        page.Controls.Add(_listA);
        page.Controls.Add(bar);
        page.Controls.Add(warn);
        return page;
    }

    private void FillA()
    {
        var rows = OrphansPresenter.FilterA(_orphans, _filterA.Text);

        _listA.BeginUpdate();
        _listA.Items.Clear();
        foreach (var o in rows)
            _listA.Items.Add(new ListViewItem(OrphansPresenter.RowA(o)));
        _listA.EndUpdate();

        _countA.Text = OrphansPresenter.CountA(rows.Count, _orphans);
    }

    private void ExportA() =>
        // Exportiert wird, was in der Liste steht — bei gesetztem Filter also die Treffer.
        CsvExport.Save(this, "objekt-leichen.csv",
            OrphansPresenter.ColumnsA,
            OrphansPresenter.FilterA(_orphans, _filterA.Text).Select(OrphansPresenter.RowA));

    // ---------------------------------------------------------------- Analyse B

    private TabPage BuildAnalysisB()
    {
        var page = new TabPage("B — Unbenutzte User-Datenpunkte") { Padding = new Padding(8), UseVisualStyleBackColor = true };

        var warn = BuildWarning(OrphansPresenter.WarningB);

        var bar = TabLayout.TopBar(40);

        var lbl = new Label { Text = "Filter:", Location = new Point(0, 11), Size = new Size(44, 20) };
        _filterB.Location = new Point(46, 8);
        _filterB.Size = new Size(300, 24);
        _filterB.PlaceholderText = "Datenpunkt-ID …";
        _filterB.TextChanged += (_, _) => FillB();

        _showAllB.Text = "Alle geprüften Datenpunkte anzeigen";
        _showAllB.Location = new Point(360, 10);
        _showAllB.Size = new Size(260, 22);
        _showAllB.CheckedChanged += (_, _) => FillB();

        _countB.Location = new Point(630, 11);
        _countB.Size = new Size(400, 20);
        _countB.ForeColor = SystemColors.GrayText;

        _csvB.Text = "Als CSV exportieren";
        _csvB.Size = new Size(160, 26);
        _csvB.Location = new Point(920, 7);
        _csvB.Click += (_, _) => ExportB();

        bar.Controls.AddRange(new Control[] { lbl, _filterB, _showAllB, _countB, _csvB });

        // Rechtsbündigen Button zuverlässig positionieren: Anchor=Right greift in einem
        // verschachtelten TabControl (Analyse A/B/C) je nach Anzeige-Zeitpunkt daneben und
        // schiebt den Button bei maximiertem Fenster aus dem Sichtbereich.
        bar.SizeChanged += (_, _) =>
        {
            _csvB.Left = bar.ClientSize.Width - _csvB.Width - 4;
            _countB.Width = Math.Max(60, _csvB.Left - _countB.Location.X - 8);
        };

        _listB.Dock = DockStyle.Fill;
        _listB.View = View.Details;
        _listB.FullRowSelect = true;
        _listB.GridLines = true;
        _listB.Columns.Add("Datenpunkt-ID", 380);
        _listB.Columns.Add("Name", 170);
        _listB.Columns.Add("In Skripten", 90);
        _listB.Columns.Add("In VIS", 80);
        _listB.Columns.Add("Alias-Ziel", 80);
        _listB.Columns.Add("Logging", 70);
        _listB.Columns.Add("In Chart", 70);
        _listB.Columns.Add("Zuletzt geändert", 180);
        _listB.Columns.Add("Bewertung", 170);
        _listB.MultiSelect = true;
        _listB.HideSelection = false;
        ListViewCopy.Attach(_listB);
        ListViewSort.Attach(_listB);

        page.Controls.Add(_listB);
        page.Controls.Add(bar);
        page.Controls.Add(warn);
        return page;
    }

    private void FillB()
    {
        var rows = OrphansPresenter.FilterB(_unused, _showAllB.Checked, _filterB.Text);

        _listB.BeginUpdate();
        _listB.Items.Clear();
        foreach (var u in rows)
        {
            var item = new ListViewItem(OrphansPresenter.DisplayRowB(u));

            item.ForeColor = OrphansPresenter.EmphasisB(u) switch
            {
                RowEmphasis.Muted => SystemColors.GrayText,
                RowEmphasis.Warn => Color.DarkOrange,
                RowEmphasis.Problem => Color.Firebrick,
                _ => SystemColors.ControlText
            };

            _listB.Items.Add(item);
        }
        _listB.EndUpdate();

        _countB.Text = OrphansPresenter.CountB(_unused, _states?.HasStates == true);
    }

    private void ExportB() =>
        CsvExport.Save(this, "verwaiste-datenpunkte.csv",
            OrphansPresenter.CsvColumnsB,
            OrphansPresenter.FilterB(_unused, _showAllB.Checked, _filterB.Text)
                            .Select(OrphansPresenter.RowB));

    // ---------------------------------------------------------------- Analyse C

    private TabPage BuildAnalysisC()
    {
        var page = new TabPage("C — States") { Padding = new Padding(8), UseVisualStyleBackColor = true };

        var warn = BuildWarning(OrphansPresenter.WarningC);

        var bar = TabLayout.TopBar(40);

        var lblView = new Label { Text = "Sicht:", Location = new Point(0, 11), Size = new Size(42, 20) };
        _viewC.Location = new Point(44, 8);
        _viewC.Size = new Size(290, 24);
        _viewC.DropDownStyle = ComboBoxStyle.DropDownList;
        _viewC.Items.AddRange(OrphansPresenter.ViewLabelsC.Cast<object>().ToArray());
        _viewC.SelectedIndex = 0;
        _viewC.SelectedIndexChanged += (_, _) => FillC();

        var lblFilter = new Label { Text = "Filter:", Location = new Point(344, 11), Size = new Size(44, 20) };
        _filterC.Location = new Point(390, 8);
        _filterC.Size = new Size(170, 24);
        _filterC.PlaceholderText = "Datenpunkt-ID …";
        _filterC.TextChanged += (_, _) => FillC();

        _countC.Location = new Point(568, 11);
        _countC.Size = new Size(210, 20);
        _countC.ForeColor = SystemColors.GrayText;

        // Aufräum-Skript für Waisen-States erzeugen (nur bei „States ohne Objekt" sinnvoll,
        // arbeitet aber immer auf der Werte-Leichen-Liste, egal welche Sicht gerade aktiv ist).
        _cleanupC.Text = "Aufräum-Skript erzeugen …";
        _cleanupC.Size = new Size(180, 26);
        _cleanupC.Location = new Point(790, 7);
        _cleanupC.Enabled = false;
        _cleanupC.Click += (_, _) => OpenCleanupDialog();

        _csvC.Text = "Als CSV exportieren";
        _csvC.Size = new Size(160, 26);
        _csvC.Location = new Point(980, 7);
        _csvC.Click += (_, _) => ExportC();

        bar.Controls.AddRange(new Control[] { lblView, _viewC, lblFilter, _filterC, _countC, _cleanupC, _csvC });

        // Rechtsbündige Buttons zuverlässig positionieren (siehe Kommentar in Analyse B):
        // im verschachtelten TabControl greift Anchor=Right nicht verlässlich.
        bar.SizeChanged += (_, _) =>
        {
            _csvC.Left = bar.ClientSize.Width - _csvC.Width - 4;
            _cleanupC.Left = _csvC.Left - _cleanupC.Width - 8;
            _countC.Width = Math.Max(60, _cleanupC.Left - _countC.Location.X - 8);
        };

        // Altersverteilung als schmale Zeile über der Tabelle — sie ordnet jede Sicht ein.
        _statsC.Dock = DockStyle.Top;
        _statsC.Height = 38;
        _statsC.ForeColor = SystemColors.GrayText;
        _statsC.Padding = new Padding(2, 4, 2, 2);

        _listC.Dock = DockStyle.Fill;
        _listC.View = View.Details;
        _listC.FullRowSelect = true;
        _listC.GridLines = true;
        _listC.HideSelection = false;
        _listC.MultiSelect = true;
        _listC.Columns.Add("Datenpunkt-ID", 420);
        _listC.Columns.Add("Name", 180);
        _listC.Columns.Add("Zuletzt geändert", 190);
        // Aus common.write: unterscheidet reine Lieferanten (Sensoren) von schreibbaren
        // Datenpunkten - Rueckfrage aus der Praxis zur Sicht "Objekte ohne Wert".
        _listC.Columns.Add("Schreibbar", 90);
        _listC.Columns.Add("Quelle", 170);
        _listC.Columns.Add("Qualität", 180);
        _listC.Columns.Add("Quittiert", 80);
        ListViewCopy.Attach(_listC);
        ListViewSort.Attach(_listC);

        page.Controls.Add(_listC);
        page.Controls.Add(_statsC);
        page.Controls.Add(bar);
        page.Controls.Add(warn);
        return page;
    }

    private StateView CurrentStateView => (StateView)Math.Max(0, _viewC.SelectedIndex);

    private List<StateRow> CurrentStateRows() => OrphansPresenter.RowsC(_states, CurrentStateView);

    private void FillC()
    {
        if (_states is null) return;

        var all = CurrentStateRows();
        var rows = OrphansPresenter.FilterC(all, _filterC.Text);
        var limit = OrphansPresenter.LimitC(CurrentStateView, rows.Count);

        _listC.BeginUpdate();
        _listC.Items.Clear();

        var shown = 0;
        foreach (var r in rows)
        {
            if (shown++ >= limit) break;

            var item = new ListViewItem(OrphansPresenter.DisplayRowC(r));

            item.ForeColor = OrphansPresenter.EmphasisC(r) switch
            {
                RowEmphasis.Warn => Color.DarkOrange,
                RowEmphasis.Problem => Color.Firebrick,
                _ => SystemColors.ControlText
            };

            _listC.Items.Add(item);
        }
        _listC.EndUpdate();

        _countC.Text = OrphansPresenter.CountC(rows.Count, all.Count,
                                               _filterC.Text.Trim().Length > 0, limit);
        _statsC.Text = OrphansPresenter.StatsC(_states).Replace("\n", "\r\n");
    }

    /// <summary>
    /// Exportiert die gefilterte Sicht. Die Anzeigegrenze von 2.000 Zeilen (Sicht
    /// „Älteste") gilt dabei bewusst nicht — die CSV enthält alle Treffer.
    /// </summary>
    private void ExportC() =>
        CsvExport.Save(this, OrphansPresenter.CsvNameC(CurrentStateView),
            OrphansPresenter.CsvColumnsC,
            OrphansPresenter.FilterC(CurrentStateRows(), _filterC.Text)
                            .Select(OrphansPresenter.RowC));

    /// <summary>
    /// Öffnet den Dialog, der aus den Werte-Leichen ein Aufräum-Skript erzeugt. Die
    /// Namensräume kommen immer aus „States ohne Objekt", unabhängig von der gerade
    /// gewählten Sicht.
    /// </summary>
    private void OpenCleanupDialog()
    {
        if (_states is null) return;

        var groups = _states.StatesWithoutObject
            .GroupBy(r => r.Namespace, StringComparer.Ordinal)
            .Select(g => (Namespace: g.Key, Ids: (IReadOnlyList<string>)g.Select(r => r.Id).ToList()))
            .ToList();

        if (groups.Count == 0)
        {
            MessageBox.Show(this, "Keine States ohne Objekt gefunden — es gibt nichts aufzuräumen.",
                "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new CleanupScriptDialog(groups, _data?.SourceFile);
        dlg.ShowDialog(this);
    }

    // ---------------------------------------------------------------- Gemeinsam

    public void SetAvailable(bool available)
    {
        _placeholder.Visible = !available;
        _sub.Visible = available;

        if (!available)
        {
            _placeholder.Text = _data is null
                ? "Kein Backup geladen.\r\n\r\nBitte oben eine Datei öffnen oder hineinziehen."
                : "Für die Verwaisten-Analyse wird ein Voll-Backup benötigt.\r\n\r\n" +
                  "Die geladene Datei enthält nur Skripte —\r\n" +
                  "verfügbar ist damit nur der Tab „Skripte\".";
            _placeholder.BringToFront();
        }
    }

    public void SetData(BackupData data, AnalysisResults? fertig = null)
    {
        _data = data;

        if (data.Kind != BackupKind.Full)
        {
            _orphans = new List<OrphanObject>();
            _unused = new List<UnusedDatapoint>();
            _states = null;
            _listA.Items.Clear();
            _listB.Items.Clear();
            _listC.Items.Clear();
            _cleanupC.Enabled = false;
            return;
        }

        if (fertig is { Orphans: { } o, Unused: { } u, States: { } st })
        {
            _orphans = o;
            _unused = u;
            _states = st;
        }
        else
        {
        Cursor = Cursors.WaitCursor;
        try
        {
            _orphans = OrphanAnalyzer.FindOrphanObjects(data);
            _unused = OrphanAnalyzer.FindUnusedDatapoints(data);
            _states = StateAnalyzer.Analyze(data);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
        }

        // Aufräum-Skript nur anbieten, wenn es überhaupt Werte-Leichen gibt.
        _cleanupC.Enabled = _states.StatesWithoutObject.Count > 0;

        // Ohne VIS-Views fehlt eine der vier Prüfungen — das muss sichtbar sein.
        if (data.VisViews.Count == 0)
            _showAllB.Text = "Alle anzeigen (Achtung: keine VIS-Views im Backup)";

        // Ältere Backitup-Stände ohne states.jsonl: die Zeitangaben bleiben leer, das darf
        // nicht wie „nie beschrieben" aussehen.
        _sub.TabPages[2].Text = _states.HasStates ? "C — States" : "C — States (nicht im Backup)";

        FillA();
        FillB();
        FillC();
    }
}

using IobBackupAnalyzer.Core;

namespace IobBackupAnalyzer.App;

/// <summary>
/// Kreuzreferenz zwischen Skripten und Datenpunkten, in beiden Richtungen: oben die Liste,
/// unten die Gegenseite des gewählten Eintrags.
///
/// Die zwei Fragen, für die dieser Tab gebaut ist: „Wer schreibt diesen Wert außer mir noch?"
/// und „Was habe ich angelegt und nie benutzt?". Beide beantwortet erst die Verbindung
/// zwischen beiden Listen — deshalb Master und Detail statt zweier getrennter Tabellen.
/// </summary>
public sealed class UsageTab : UserControl
{
    private readonly Label _summary = new();
    private readonly ComboBox _view = new();
    private readonly ComboBox _stateFilter = new();
    private readonly CheckBox _onlyWithStates = new();
    private readonly TextBox _filter = new();
    private readonly Label _count = new();
    private readonly Button _csv = new();

    /// <summary>Farbige Legende unter der Bedienleiste; wird je Sicht neu aufgebaut.</summary>
    private readonly FlowLayoutPanel _legend = new();

    private readonly ListView _master = new();
    private readonly Label _detailHeader = new();
    private readonly ListView _detail = new();
    private readonly Label _placeholder = new();

    private UsageReport? _report;

    /// <summary>
    /// Doppelklick auf ein Skript — übergeben wird dessen ioBroker-ID. Das Hauptfenster
    /// wechselt daraufhin in den Skripte-Tab und wählt es dort aus. Der Weg über ein
    /// Ereignis statt eines direkten Zugriffs hält die beiden Tabs voneinander unabhängig.
    /// </summary>
    public event Action<string>? ScriptRequested;

    public UsageTab()
    {
        BuildUi();
    }

    private UsageDirection CurrentView => (UsageDirection)_view.SelectedIndex;

    /// <summary>
    /// Die Verweise ins Leere, einmal je geladenem Backup. Die Suche geht durch jeden
    /// Skriptquelltext; sie bei jedem Umschalten der Sicht zu wiederholen wäre Verschwendung.
    /// </summary>
    private List<DeadRefRow> _deadRefs = new();
    private BackupData? _deadRefsFor;
    private BackupData? _data;
    private Label _hint = new();

    private List<DeadRefRow> DeadRefs
    {
        get
        {
            if (!ReferenceEquals(_deadRefsFor, _data))
            {
                _deadRefs = _data is null ? new List<DeadRefRow>() : ExtraAnalyzer.Analyze(_data);
                _deadRefsFor = _data;
            }

            return _deadRefs;
        }
    }

    private void BuildUi()
    {
        Padding = new Padding(8);

        var head = TabLayout.TopBar(150);

        _summary.Location = new Point(0, 0);
        _summary.Size = new Size(1100, 34);
        _summary.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        _hint = new Label
        {
            Location = new Point(0, 36),
            Size = new Size(1100, 34),
            ForeColor = SystemColors.GrayText,
            Text = UsagePresenter.Warning,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        var lblView = new Label { Text = "Sicht:", Location = new Point(0, 79), Size = new Size(42, 20) };
        _view.Location = new Point(44, 76);
        _view.Size = new Size(200, 24);
        _view.DropDownStyle = ComboBoxStyle.DropDownList;
        _view.Items.AddRange(UsagePresenter.ViewLabels.Cast<object>().ToArray());
        _view.SelectedIndex = 0;
        _view.SelectedIndexChanged += (_, _) => SwitchView();

        // Beide Zusatzfilter liegen übereinander; sichtbar ist der zur gewählten Sicht.
        _stateFilter.Location = new Point(254, 76);
        _stateFilter.Size = new Size(250, 24);
        _stateFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        _stateFilter.Items.AddRange(UsagePresenter.StateFilterLabels.Cast<object>().ToArray());
        _stateFilter.SelectedIndex = 0;
        _stateFilter.Visible = false;
        _stateFilter.SelectedIndexChanged += (_, _) => Fill();

        _onlyWithStates.Text = "Nur Skripte mit Datenpunkten";
        _onlyWithStates.Location = new Point(254, 78);
        _onlyWithStates.Size = new Size(220, 22);
        _onlyWithStates.Checked = true;
        _onlyWithStates.CheckedChanged += (_, _) => Fill();

        var lblFilter = new Label { Text = "Filter:", Location = new Point(514, 79), Size = new Size(44, 20) };
        _filter.Location = new Point(560, 76);
        _filter.Size = new Size(360, 24);
        _filter.PlaceholderText = "Skript oder Datenpunkt …";
        _filter.TextChanged += (_, _) => Fill();

        // Eigene Zeile statt neben dem Filter: Der Zähltext wird lang („173 von 200 Skripten
        // verwenden Datenpunkte · 1.587 Verbindungen") und liefe sonst in den CSV-Knopf.
        _count.Location = new Point(0, 104);
        _count.Size = new Size(1090, 20);
        _count.ForeColor = SystemColors.GrayText;
        _count.AutoEllipsis = true;
        _count.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        _csv.Text = "Als CSV exportieren";
        _csv.Size = new Size(160, 26);
        _csv.Location = TabLayout.RightAligned(160, 75);
        _csv.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _csv.Click += (_, _) => ExportCsv();

        // Legende in den echten Zeilenfarben: „Rot" zu lesen hilft wenig, wenn daneben
        // nicht dasselbe Rot steht wie in der Tabelle.
        _legend.Location = new Point(0, 126);
        _legend.Size = new Size(1090, 22);
        _legend.WrapContents = false;
        _legend.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        head.Controls.AddRange(new Control[]
            { _summary, _hint, lblView, _view, _stateFilter, _onlyWithStates, lblFilter, _filter,
              _count, _legend, _csv });

        // ---------- obere Liste ----------
        ConfigureList(_master);
        _master.SelectedIndexChanged += (_, _) => ShowDetail();
        _master.MouseDoubleClick += (_, e) => JumpToScript(HitTag(_master, e));

        // ---------- untere Liste ----------
        _detailHeader.Dock = DockStyle.Top;
        _detailHeader.Height = 24;
        _detailHeader.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        _detailHeader.TextAlign = ContentAlignment.MiddleLeft;

        ConfigureList(_detail);
        // In der Datenpunkt-Sicht stehen unten die Skripte — dort führt der Doppelklick
        // genauso zum Skript wie oben in der Skript-Sicht. In der Skript-Sicht stehen unten
        // Datenpunkte; ein Sprung führte dort nur zum ohnehin gewählten Skript zurück.
        _detail.MouseDoubleClick += (_, e) =>
        {
            if (CurrentView == UsageDirection.ByState) JumpToScript(HitTag(_detail, e));
        };

        var detailPanel = new Panel { Dock = DockStyle.Fill };
        detailPanel.Controls.Add(_detail);
        detailPanel.Controls.Add(_detailHeader);

        // MinSizes erst setzen, wenn genug Höhe da ist (siehe AliasTab): am noch ungesizten
        // Control würde ein großes Panel2MinSize sofort werfen.
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        var splitInit = false;
        split.SizeChanged += (_, _) =>
        {
            if (splitInit) return;
            const int p1min = 140, p2min = 150;
            if (split.Height < p1min + p2min + split.SplitterWidth + 20) return;
            split.Panel1MinSize = p1min;
            split.Panel2MinSize = p2min;
            split.SplitterDistance = Math.Clamp(split.Height * 3 / 5, p1min, split.Height - p2min - split.SplitterWidth);
            splitInit = true;
        };
        split.Panel1.Controls.Add(_master);
        split.Panel2.Controls.Add(detailPanel);

        _placeholder.Dock = DockStyle.Fill;
        _placeholder.TextAlign = ContentAlignment.MiddleCenter;
        _placeholder.ForeColor = SystemColors.GrayText;
        _placeholder.Text = "Kein Backup geladen.";

        Controls.Add(split);
        Controls.Add(_placeholder);
        Controls.Add(head);

        SwitchView();
    }

    /// <summary>
    /// Die getroffene Zeile — bewusst über die Mausposition und nicht über die Auswahl:
    /// Ein Doppelklick auf die leere Fläche unter der letzten Zeile lässt die bisherige
    /// Auswahl stehen und hätte sonst einen Sprung ausgelöst, den niemand wollte.
    /// </summary>
    private static object? HitTag(ListView list, MouseEventArgs e) =>
        list.HitTest(e.Location).Item?.Tag;

    /// <summary>
    /// Meldet das angeklickte Skript nach außen. Zeilen ohne Skriptbezug — Adapter-Instanzen
    /// in der unteren Liste, Datenpunkte in der oberen — lösen bewusst nichts aus.
    /// </summary>
    private void JumpToScript(object? tag)
    {
        var id = tag switch
        {
            ScriptUsage s => s.ScriptId,
            UsageLink { Source: UsageSource.Skript } l => l.SourceId,
            _ => null
        };

        if (id is not null) ScriptRequested?.Invoke(id);
    }

    private static void ConfigureList(ListView list)
    {
        list.Dock = DockStyle.Fill;
        list.View = View.Details;
        list.FullRowSelect = true;
        list.GridLines = true;
        list.HideSelection = false;
        list.MultiSelect = true;
        ListViewCopy.Attach(list);
        ListViewSort.Attach(list);
    }

    public void SetAvailable(bool available)
    {
        _placeholder.Visible = !available;
        if (!available)
        {
            _placeholder.Text = _report is null
                ? "Kein Backup geladen.\r\n\r\nBitte oben eine Datei öffnen oder hineinziehen."
                : "Für die Verwendungsübersicht wird ein Voll-Backup benötigt.\r\n\r\n" +
                  "Ohne Objektbestand lässt sich nicht entscheiden, ob eine Zeichenkette im " +
                  "Skript wirklich ein Datenpunkt ist.";
            _placeholder.BringToFront();
        }
    }

    public void SetData(BackupData data, AnalysisResults? fertig = null)
    {
        _data = data.Kind == BackupKind.Full ? data : null;

        if (data.Kind != BackupKind.Full)
        {
            _report = null;
            _master.Items.Clear();
            _detail.Items.Clear();
            _summary.Text = "";
            return;
        }

        // Im Regelfall ist das Ergebnis schon im Hintergrund gerechnet worden; nur wenn es
        // fehlt (Bildschirmfotos, Prüfläufe), wird hier nachgerechnet.
        if (fertig?.Usage is { } vorberechnet)
        {
            _report = vorberechnet;
        }
        else
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                _report = UsageAnalyzer.Analyze(data);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        _summary.Text = UsagePresenter.Stats(_report).Replace("\n", "\r\n");
        Fill();
    }

    /// <summary>Spalten und Zusatzfilter an die gewählte Richtung anpassen.</summary>
    private void SwitchView()
    {
        var tote = CurrentView == UsageDirection.DeadRefs;
        var byScript = CurrentView == UsageDirection.ByScript;

        // In der Sicht „Verweise ins Leere" gibt es keine Gegenseite zum Anklicken und
        // nichts zu filtern: Die Liste ist die vollständige Aussage.
        _onlyWithStates.Visible = byScript;
        _stateFilter.Visible = !byScript && !tote;
        _filter.Visible = !tote;
        _detail.Visible = !tote;
        _detailHeader.Visible = !tote;

        SetColumns(_master, tote ? UsagePresenter.ColumnsDeadRefs
                                 : byScript ? UsagePresenter.ColumnsScripts : UsagePresenter.ColumnsStates);

        if (!tote)
            SetColumns(_detail, byScript ? UsagePresenter.ColumnsScriptDetail : UsagePresenter.ColumnsStateDetail);

        // Kennzahlenzeile und Warntext gehören zur Sicht, nicht zum Backup.
        _hint.Text = tote ? UsagePresenter.DeadRefWarning : UsagePresenter.Warning;

        if (_report is not null)
            _summary.Text = (tote ? UsagePresenter.StatsDeadRefs(DeadRefs) : UsagePresenter.Stats(_report))
                            .Replace("\n", "\r\n");

        ShowLegend(tote ? UsagePresenter.DeadRefLegend
                        : byScript ? UsagePresenter.ScriptLegend : UsagePresenter.StateLegend);

        Fill();
    }

    private static void SetColumns(ListView list, string[] columns)
    {
        list.BeginUpdate();
        list.Items.Clear();
        list.Columns.Clear();

        foreach (var c in columns)
        {
            // Die erste Spalte trägt die ID oder den Pfad und braucht spürbar mehr Platz.
            var isFirst = list.Columns.Count == 0;
            var width = isFirst ? 340
                : c.Length <= 8 ? 80
                : 160;
            list.Columns.Add(c, width);
        }

        list.EndUpdate();
    }

    private void Fill()
    {
        if (_report is null) return;

        _master.BeginUpdate();
        _master.Items.Clear();

        if (CurrentView == UsageDirection.DeadRefs)
        {
            foreach (var r in DeadRefs)
                _master.Items.Add(new ListViewItem(UsagePresenter.RowDeadRef(r))
                {
                    Tag = r,
                    ForeColor = ColorOf(UsagePresenter.EmphasisDeadRef(r))
                });

            _count.Text = UsagePresenter.CountDeadRefs(DeadRefs.Count);
        }
        else if (CurrentView == UsageDirection.ByScript)
        {
            var rows = UsagePresenter.FilterScripts(_report.Scripts, _onlyWithStates.Checked, _filter.Text);
            foreach (var s in rows)
                _master.Items.Add(new ListViewItem(UsagePresenter.RowScript(s))
                {
                    Tag = s,
                    ForeColor = ColorOf(UsagePresenter.EmphasisScript(s))
                });

            _count.Text = UsagePresenter.CountScripts(rows.Count, _report);
        }
        else
        {
            var rows = CurrentStates();
            foreach (var s in rows)
                _master.Items.Add(new ListViewItem(UsagePresenter.RowState(s))
                {
                    Tag = s,
                    ForeColor = ColorOf(UsagePresenter.EmphasisState(s))
                });

            _count.Text = UsagePresenter.CountStates(rows.Count, _report.States.Count, _report);
        }

        _master.EndUpdate();

        ShowDetail();
    }

    private List<StateUsage> CurrentStates() =>
        UsagePresenter.FilterStates(_report!.States,
            (UsageStateFilter)_stateFilter.SelectedIndex, _filter.Text);

    private void ShowLegend((RowEmphasis Emphasis, string Text)[] entries)
    {
        _legend.Controls.Clear();

        foreach (var (emphasis, text) in entries)
            _legend.Controls.Add(new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = ColorOf(emphasis),
                Margin = new Padding(0, 3, 18, 0)
            });

        // Der Sprung in den Skripte-Tab ist ohne Hinweis nicht zu erraten.
        _legend.Controls.Add(new Label
        {
            Text = UsagePresenter.JumpHint,
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(12, 3, 0, 0)
        });
    }

    private static Color ColorOf(RowEmphasis emphasis) => emphasis switch
    {
        RowEmphasis.Muted => SystemColors.GrayText,
        RowEmphasis.Warn => Color.DarkOrange,
        RowEmphasis.Problem => Color.Firebrick,
        _ => SystemColors.ControlText
    };

    private void ShowDetail()
    {
        _detail.BeginUpdate();
        _detail.Items.Clear();

        var selected = _master.SelectedItems.Count > 0 ? _master.SelectedItems[0].Tag : null;

        switch (selected)
        {
            case ScriptUsage s:
                foreach (var l in s.Links)
                    _detail.Items.Add(new ListViewItem(UsagePresenter.RowScriptDetail(l)) { Tag = l });
                _detailHeader.Text = UsagePresenter.DetailTitle(UsageDirection.ByScript, s.DisplayPath);
                break;

            case StateUsage st:
                foreach (var l in st.Links)
                    _detail.Items.Add(new ListViewItem(UsagePresenter.RowStateDetail(l)) { Tag = l });
                _detailHeader.Text = UsagePresenter.DetailTitle(UsageDirection.ByState, st.Id);
                break;

            default:
                _detailHeader.Text = UsagePresenter.DetailTitle(CurrentView, null);
                break;
        }

        _detail.EndUpdate();
    }

    /// <summary>
    /// Exportiert die obere Liste in ihrer aktuellen Filterung. In der Datenpunkt-Sicht
    /// kommt eine Spalte mit allen beteiligten Skripten dazu — sonst wäre gerade die
    /// interessante Information nicht in der Datei.
    /// </summary>
    private void ExportCsv()
    {
        if (_report is null) return;

        if (CurrentView == UsageDirection.ByScript)
        {
            var rows = UsagePresenter.FilterScripts(_report.Scripts, _onlyWithStates.Checked, _filter.Text);
            CsvExport.Save(this, UsagePresenter.CsvName(UsageDirection.ByScript),
                UsagePresenter.ColumnsScripts, rows.Select(UsagePresenter.RowScript));
        }
        else
        {
            CsvExport.Save(this, UsagePresenter.CsvName(UsageDirection.ByState),
                UsagePresenter.CsvColumnsStates, CurrentStates().Select(UsagePresenter.CsvRowState));
        }
    }
}

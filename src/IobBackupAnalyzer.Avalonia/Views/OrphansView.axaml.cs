using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using IobBackupAnalyzer.Core;

namespace IobBackupAnalyzer.Avalonia.Views;

/// <summary>
/// Säule 3: Verwaiste Datenpunkte — Analyse A (Objekte ohne Instanz), B (unbenutzte
/// User-Datenpunkte) und C (States) in je einem Untertab. Texte, Filter, Sichten und
/// Einstufungen kommen aus <see cref="OrphansPresenter"/>.
/// </summary>
public partial class OrphansView : UserControl
{
    private readonly TabControl _sub;
    private readonly TextBlock _placeholder;

    private readonly TextBox _filterA;
    private readonly TextBlock _countA;
    private readonly Button _csvA;
    private readonly DataGrid _listA;

    private readonly TextBox _filterB;
    private readonly CheckBox _showAllB;
    private readonly TextBlock _countB;
    private readonly Button _csvB;
    private readonly DataGrid _listB;

    private readonly ComboBox _viewC;
    private readonly TextBox _filterC;
    private readonly TextBlock _countC;
    private readonly TextBlock _statsC;
    private readonly Button _csvC;
    private readonly Button _cleanupC;
    private readonly DataGrid _listC;
    private readonly TabItem _tabC;

    private BackupData? _data;
    private List<OrphanObject> _orphans = new();
    private List<UnusedDatapoint> _unused = new();
    private StateReport? _states;

    public OrphansView()
    {
        AvaloniaXamlLoader.Load(this);
        TableLayout.FillLastColumn(this);

        _sub = this.FindControl<TabControl>("Sub")!;
        _placeholder = this.FindControl<TextBlock>("PlaceholderText")!;

        _filterA = this.FindControl<TextBox>("FilterA")!;
        _countA = this.FindControl<TextBlock>("CountA")!;
        _csvA = this.FindControl<Button>("CsvA")!;
        _listA = this.FindControl<DataGrid>("ListA")!;

        _filterB = this.FindControl<TextBox>("FilterB")!;
        _showAllB = this.FindControl<CheckBox>("ShowAllB")!;
        _countB = this.FindControl<TextBlock>("CountB")!;
        _csvB = this.FindControl<Button>("CsvB")!;
        _listB = this.FindControl<DataGrid>("ListB")!;

        _viewC = this.FindControl<ComboBox>("ViewC")!;
        _filterC = this.FindControl<TextBox>("FilterC")!;
        _countC = this.FindControl<TextBlock>("CountC")!;
        _statsC = this.FindControl<TextBlock>("StatsC")!;
        _csvC = this.FindControl<Button>("CsvC")!;
        _cleanupC = this.FindControl<Button>("CleanupC")!;
        _listC = this.FindControl<DataGrid>("ListC")!;
        _tabC = this.FindControl<TabItem>("TabC")!;

        this.FindControl<TextBlock>("WarnA")!.Text = OrphansPresenter.WarningA;
        this.FindControl<TextBlock>("WarnB")!.Text = OrphansPresenter.WarningB;
        this.FindControl<TextBlock>("WarnC")!.Text = OrphansPresenter.WarningC;

        _showAllB.Content = OrphansPresenter.ShowAllLabelB(true);
        _viewC.ItemsSource = OrphansPresenter.ViewLabelsC;
        _viewC.SelectedIndex = 0;

        // Alle drei Exporte geben aus, was in der Liste steht — bei gesetztem Filter also
        // nur die Treffer. Die Anzeigegrenze von 2.000 Zeilen in Sicht C gilt dabei nicht:
        // die CSV enthält weiterhin alle Treffer.
        _filterA.TextChanged += (_, _) => FillA();
        _csvA.Click += async (_, _) => await Dialogs.SaveCsvAsync(this, "objekt-leichen.csv",
            OrphansPresenter.ColumnsA,
            OrphansPresenter.FilterA(_orphans, _filterA.Text).Select(OrphansPresenter.RowA).ToList());

        _filterB.TextChanged += (_, _) => FillB();
        _showAllB.IsCheckedChanged += (_, _) => FillB();
        _csvB.Click += async (_, _) => await Dialogs.SaveCsvAsync(this, "verwaiste-datenpunkte.csv",
            OrphansPresenter.CsvColumnsB,
            OrphansPresenter.FilterB(_unused, _showAllB.IsChecked == true, _filterB.Text)
                            .Select(OrphansPresenter.RowB).ToList());

        _filterC.TextChanged += (_, _) => FillC();
        _viewC.SelectionChanged += (_, _) => FillC();
        _csvC.Click += async (_, _) => await Dialogs.SaveCsvAsync(this,
            OrphansPresenter.CsvNameC(CurrentStateView),
            OrphansPresenter.CsvColumnsC,
            OrphansPresenter.FilterC(OrphansPresenter.RowsC(_states, CurrentStateView), _filterC.Text)
                            .Select(OrphansPresenter.RowC).ToList());
        _cleanupC.Click += async (_, _) => await OpenCleanupDialogAsync();

        _listB.LoadingRow += (_, e) =>
        {
            var u = e.Row.DataContext as UnusedDatapoint;
            SetEmphasis(e.Row, u is null ? RowEmphasis.None : OrphansPresenter.EmphasisB(u));
        };
        _listC.LoadingRow += (_, e) =>
        {
            var r = e.Row.DataContext as StateRow;
            SetEmphasis(e.Row, r is null ? RowEmphasis.None : OrphansPresenter.EmphasisC(r));
        };

        SetData(null);
    }

    /// <summary>
    /// Setzt die Stilklasse zur Einstufung. Zeilen werden beim Scrollen wiederverwendet —
    /// alle nicht zutreffenden Klassen müssen deshalb aktiv entfernt werden, sonst bleibt
    /// eine Einfärbung an der falschen Zeile hängen.
    /// </summary>
    private static void SetEmphasis(DataGridRow row, RowEmphasis emphasis)
    {
        Apply("gedaempft", emphasis == RowEmphasis.Muted);
        Apply("warnung", emphasis == RowEmphasis.Warn);
        Apply("problem", emphasis == RowEmphasis.Problem);

        void Apply(string name, bool on)
        {
            if (on) { if (!row.Classes.Contains(name)) row.Classes.Add(name); }
            else row.Classes.Remove(name);
        }
    }

    private StateView CurrentStateView => (StateView)Math.Max(0, _viewC.SelectedIndex);

    public void SetData(BackupData? data, AnalysisResults? fertig = null)
    {
        _data = data;

        if (data is null || data.Kind != BackupKind.Full)
        {
            _orphans = new List<OrphanObject>();
            _unused = new List<UnusedDatapoint>();
            _states = null;
            _listA.ItemsSource = null;
            _listB.ItemsSource = null;
            _listC.ItemsSource = null;
            _countA.Text = _countB.Text = _countC.Text = _statsC.Text = "";
            _cleanupC.IsEnabled = false;

            _placeholder.Text = data is null
                ? "Kein Backup geladen.\n\nBitte oben eine Datei öffnen oder hineinziehen."
                : "Für die Verwaisten-Analyse wird ein Voll-Backup benötigt.\n\n" +
                  "Die geladene Datei enthält nur Skripte —\n" +
                  "verfügbar ist damit nur der Tab „Skripte\".";
            ShowPlaceholder(true);
            return;
        }

        _orphans = fertig?.Orphans ?? OrphanAnalyzer.FindOrphanObjects(data);
        _unused = fertig?.Unused ?? OrphanAnalyzer.FindUnusedDatapoints(data);
        _states = fertig?.States ?? StateAnalyzer.Analyze(data);

        // Aufräum-Skript nur anbieten, wenn es überhaupt Werte-Leichen gibt.
        _cleanupC.IsEnabled = _states.StatesWithoutObject.Count > 0;

        // Ohne VIS-Views fehlt eine der Prüfungen — das muss sichtbar sein.
        _showAllB.Content = OrphansPresenter.ShowAllLabelB(data.VisViews.Count > 0);

        // Ältere Backitup-Stände ohne states.jsonl: die Zeitangaben bleiben leer, das darf
        // nicht wie „nie beschrieben" aussehen.
        _tabC.Header = OrphansPresenter.TabTitleC(_states.HasStates);

        ShowPlaceholder(false);
        FillA();
        FillB();
        FillC();
    }

    private void ShowPlaceholder(bool show)
    {
        _placeholder.IsVisible = show;
        _sub.IsVisible = !show;
    }

    private void FillA()
    {
        var rows = OrphansPresenter.FilterA(_orphans, _filterA.Text);
        _listA.ItemsSource = rows;
        _countA.Text = OrphansPresenter.CountA(rows.Count, _orphans);
    }

    private void FillB()
    {
        var rows = OrphansPresenter.FilterB(_unused, _showAllB.IsChecked == true, _filterB.Text);
        _listB.ItemsSource = rows;
        _countB.Text = OrphansPresenter.CountB(_unused, _states?.HasStates == true);
    }

    private void FillC()
    {
        if (_states is null) return;

        var all = OrphansPresenter.RowsC(_states, CurrentStateView);
        var rows = OrphansPresenter.FilterC(all, _filterC.Text);
        var limit = OrphansPresenter.LimitC(CurrentStateView, rows.Count);

        // Die Sicht „Älteste" umfasst alle States; vollständig dargestellt wäre die Liste
        // unbenutzbar lang. Der CSV-Export enthält immer alles.
        _listC.ItemsSource = rows.Count > limit ? rows.Take(limit).ToList() : rows;
        _countC.Text = OrphansPresenter.CountC(rows.Count, all.Count,
                                               (_filterC.Text ?? "").Trim().Length > 0, limit);
        _statsC.Text = OrphansPresenter.StatsC(_states);
    }

    /// <summary>
    /// Öffnet den Dialog, der aus den Werte-Leichen ein Aufräum-Skript erzeugt. Die
    /// Namensräume kommen immer aus „States ohne Objekt", unabhängig von der gerade
    /// gewählten Sicht.
    /// </summary>
    private async Task OpenCleanupDialogAsync()
    {
        var groups = OrphansPresenter.CleanupGroups(_states);

        if (groups.Count == 0)
        {
            await Dialogs.MessageAsync(this, "Hinweis",
                "Keine States ohne Objekt gefunden — es gibt nichts aufzuräumen.");
            return;
        }

        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        await new CleanupScriptDialog(groups, _data?.SourceFile).ShowDialog(owner);
    }
}

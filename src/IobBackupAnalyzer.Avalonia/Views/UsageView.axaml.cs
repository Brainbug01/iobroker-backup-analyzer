using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.VisualTree;
using IobBackupAnalyzer.Core;
using Core = IobBackupAnalyzer.Core;

namespace IobBackupAnalyzer.Avalonia.Views;

/// <summary>
/// Kreuzreferenz zwischen Skripten und Datenpunkten, in beiden Richtungen — inhaltlich
/// gleich zur Windows-Fassung (<c>UsageTab</c>).
///
/// Je Richtung ein eigenes Tabellenpaar statt einer Tabelle mit wechselnden Spalten: Ein
/// Avalonia-DataGrid bindet seine Spalten fest, und zur Laufzeit ausgetauschte Spalten
/// verlieren Sortierung und Breiten. Sichtbar ist immer nur das Paar der gewählten Sicht.
/// </summary>
public partial class UsageView : UserControl
{
    private readonly TextBlock _summary;
    private readonly TextBlock _warning;
    private readonly ComboBox _viewChoice;
    private readonly ComboBox _stateFilter;
    private readonly CheckBox _onlyWithStates;
    private readonly TextBox _filter;
    private readonly TextBlock _count;
    private readonly Button _csv;

    private readonly DataGrid _scriptRows;
    private readonly DataGrid _stateRows;
    private readonly DataGrid _scriptDetail;
    private readonly DataGrid _stateDetail;
    private readonly StackPanel _legend;
    private readonly TextBlock _detailHeader;
    private readonly TextBlock _placeholder;
    private readonly Grid _split;

    private UsageReport? _report;

    /// <summary>
    /// Doppelklick auf ein Skript — übergeben wird dessen ioBroker-ID. Das Hauptfenster
    /// wechselt daraufhin in den Skripte-Tab und wählt es dort aus; gleiche Verdrahtung
    /// wie in der Windows-Fassung (<c>UsageTab</c>).
    /// </summary>
    public event Action<string>? ScriptRequested;

    public UsageView()
    {
        AvaloniaXamlLoader.Load(this);
        TableLayout.FillLastColumn(this);

        _summary = this.FindControl<TextBlock>("Summary")!;
        _warning = this.FindControl<TextBlock>("Warning")!;
        _viewChoice = this.FindControl<ComboBox>("ViewChoice")!;
        _stateFilter = this.FindControl<ComboBox>("StateFilter")!;
        _onlyWithStates = this.FindControl<CheckBox>("OnlyWithStates")!;
        _filter = this.FindControl<TextBox>("Filter")!;
        _count = this.FindControl<TextBlock>("Count")!;
        _csv = this.FindControl<Button>("Csv")!;

        _scriptRows = this.FindControl<DataGrid>("ScriptRows")!;
        _stateRows = this.FindControl<DataGrid>("StateRows")!;
        _scriptDetail = this.FindControl<DataGrid>("ScriptDetail")!;
        _stateDetail = this.FindControl<DataGrid>("StateDetail")!;
        _legend = this.FindControl<StackPanel>("Legend")!;
        _detailHeader = this.FindControl<TextBlock>("DetailHeader")!;
        _placeholder = this.FindControl<TextBlock>("PlaceholderText")!;
        _split = this.FindControl<Grid>("Split")!;

        _warning.Text = UsagePresenter.Warning;

        _viewChoice.ItemsSource = UsagePresenter.ViewLabels;
        _viewChoice.SelectedIndex = 0;
        _stateFilter.ItemsSource = UsagePresenter.StateFilterLabels;
        _stateFilter.SelectedIndex = 0;
        _stateFilter.IsVisible = false;

        _viewChoice.SelectionChanged += (_, _) => SwitchView();
        _stateFilter.SelectionChanged += (_, _) => Fill();
        _onlyWithStates.IsCheckedChanged += (_, _) => Fill();
        _filter.TextChanged += (_, _) => Fill();
        _scriptRows.SelectionChanged += (_, _) => ShowDetail();
        _stateRows.SelectionChanged += (_, _) => ShowDetail();
        _csv.Click += async (_, _) => await ExportCsvAsync();

        // In der Datenpunkt-Sicht stehen die Skripte unten, darum auch auf der Detailtabelle.
        _scriptRows.DoubleTapped += (_, e) => JumpToScript(HitItem(e.Source));
        _stateDetail.DoubleTapped += (_, e) => JumpToScript(HitItem(e.Source));

        _scriptRows.LoadingRow += (_, e) => SetEmphasis(e.Row,
            e.Row.DataContext is ScriptUsage s ? UsagePresenter.EmphasisScript(s) : RowEmphasis.None);
        _stateRows.LoadingRow += (_, e) => SetEmphasis(e.Row,
            e.Row.DataContext is StateUsage st ? UsagePresenter.EmphasisState(st) : RowEmphasis.None);

        SetData(null);
    }

    /// <summary>
    /// Die gewählte Blickrichtung. Voll qualifiziert, weil diese Klasse denselben Namen
    /// trägt wie die Aufzählung in Core — der Kurzname zeigt hier auf das Steuerelement.
    /// </summary>
    private Core.UsageDirection CurrentDirection =>
        (Core.UsageDirection)Math.Max(0, _viewChoice.SelectedIndex);

    public void SetData(BackupData? data, AnalysisResults? fertig = null)
    {
        if (data is null || data.Kind != BackupKind.Full)
        {
            _report = null;
            _summary.Text = "";
            _count.Text = "";
            _scriptRows.ItemsSource = null;
            _stateRows.ItemsSource = null;
            _scriptDetail.ItemsSource = null;
            _stateDetail.ItemsSource = null;
            _placeholder.Text = data is null
                ? "Kein Backup geladen.\n\nBitte oben eine Datei öffnen oder hineinziehen."
                : "Für die Verwendungsübersicht wird ein Voll-Backup benötigt.\n\n" +
                  "Ohne Objektbestand lässt sich nicht entscheiden, ob eine Zeichenkette im " +
                  "Skript wirklich ein Datenpunkt ist.";
            ShowPlaceholder(true);
            return;
        }

        // Im Regelfall im Hintergrund vorberechnet; nur ohne Vorberechnung (Selbsttest,
        // Bildschirmfotos) wird hier gerechnet.
        _report = fertig?.Usage ?? UsageAnalyzer.Analyze(data);
        _summary.Text = UsagePresenter.Stats(_report);
        ShowPlaceholder(false);
        SwitchView();
    }

    private void ShowPlaceholder(bool show)
    {
        _placeholder.IsVisible = show;
        _split.IsVisible = !show;
    }

    private void SwitchView()
    {
        var byScript = CurrentDirection == Core.UsageDirection.ByScript;

        _scriptRows.IsVisible = byScript;
        _stateRows.IsVisible = !byScript;
        _scriptDetail.IsVisible = byScript;
        _stateDetail.IsVisible = !byScript;
        _onlyWithStates.IsVisible = byScript;
        _stateFilter.IsVisible = !byScript;

        ShowLegend(byScript ? UsagePresenter.ScriptLegend : UsagePresenter.StateLegend);

        Fill();
    }

    private List<ScriptUsage> CurrentScripts() =>
        UsagePresenter.FilterScripts(_report!.Scripts, _onlyWithStates.IsChecked == true, _filter.Text);

    private List<StateUsage> CurrentStates() =>
        UsagePresenter.FilterStates(_report!.States,
            (UsageStateFilter)Math.Max(0, _stateFilter.SelectedIndex), _filter.Text);

    private void Fill()
    {
        if (_report is null) return;

        if (CurrentDirection == Core.UsageDirection.ByScript)
        {
            var rows = CurrentScripts();
            _scriptRows.ItemsSource = rows;
            _count.Text = UsagePresenter.CountScripts(rows.Count, _report);
        }
        else
        {
            var rows = CurrentStates();
            _stateRows.ItemsSource = rows;
            _count.Text = UsagePresenter.CountStates(rows.Count, _report.States.Count, _report);
        }

        ShowDetail();
    }

    private void ShowDetail()
    {
        if (CurrentDirection == Core.UsageDirection.ByScript)
        {
            var s = _scriptRows.SelectedItem as ScriptUsage;
            _scriptDetail.ItemsSource = s?.Links;
            _detailHeader.Text = UsagePresenter.DetailTitle(
                Core.UsageDirection.ByScript, s?.DisplayPath);
        }
        else
        {
            var st = _stateRows.SelectedItem as StateUsage;
            _stateDetail.ItemsSource = st?.Links;
            _detailHeader.Text = UsagePresenter.DetailTitle(
                Core.UsageDirection.ByState, st?.Id);
        }
    }

    /// <summary>
    /// Die getroffene Zeile — bewusst über den Ursprung des Ereignisses und nicht über die
    /// Auswahl: Ein Doppelklick auf Spaltenkopf oder leere Fläche lässt die bisherige
    /// Auswahl stehen und hätte sonst einen Sprung ausgelöst, den niemand wollte.
    /// </summary>
    private static object? HitItem(object? source) =>
        (source as global::Avalonia.Visual)?.FindAncestorOfType<DataGridRow>()?.DataContext;

    /// <summary>
    /// Meldet das angeklickte Skript nach außen. Zeilen ohne Skriptbezug — Adapter-Instanzen
    /// in der unteren Liste — lösen bewusst nichts aus.
    /// </summary>
    private void JumpToScript(object? item)
    {
        var id = item switch
        {
            ScriptUsage s => s.ScriptId,
            UsageLink { Source: UsageSource.Skript } l => l.SourceId,
            _ => null
        };

        if (id is not null) ScriptRequested?.Invoke(id);
    }

    /// <summary>
    /// Legende in denselben Farben wie die Zeilen. Die Werte stehen bewusst hier und nicht
    /// als Stil-Klasse: Ein TextBlock trägt keine DataGridRow-Klasse, und zwei Definitionen
    /// derselben Farbe liefen früher oder später auseinander.
    /// </summary>
    private void ShowLegend((RowEmphasis Emphasis, string Text)[] entries)
    {
        _legend.Children.Clear();

        foreach (var (emphasis, text) in entries)
        {
            var block = new TextBlock { Text = text, FontSize = 12 };

            switch (emphasis)
            {
                case RowEmphasis.Problem:
                    block.Foreground = new SolidColorBrush(Color.Parse("#D9534F"));
                    break;
                case RowEmphasis.Warn:
                    block.Foreground = new SolidColorBrush(Color.Parse("#D98A00"));
                    break;
                case RowEmphasis.Muted:
                    block.Opacity = 0.55;
                    break;
            }

            _legend.Children.Add(block);
        }

        // Der Sprung in den Skripte-Tab ist ohne Hinweis nicht zu erraten.
        _legend.Children.Add(new TextBlock
        {
            Text = UsagePresenter.JumpHint,
            FontSize = 12,
            Opacity = 0.7,
            Margin = new global::Avalonia.Thickness(12, 0, 0, 0)
        });
    }

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

    private async Task ExportCsvAsync()
    {
        if (_report is null) return;

        if (CurrentDirection == Core.UsageDirection.ByScript)
            await Dialogs.SaveCsvAsync(this,
                UsagePresenter.CsvName(Core.UsageDirection.ByScript),
                UsagePresenter.ColumnsScripts,
                CurrentScripts().Select(UsagePresenter.RowScript).ToList());
        else
            await Dialogs.SaveCsvAsync(this,
                UsagePresenter.CsvName(Core.UsageDirection.ByState),
                UsagePresenter.CsvColumnsStates,
                CurrentStates().Select(UsagePresenter.CsvRowState).ToList());
    }
}

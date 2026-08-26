using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using IobBackupAnalyzer.Core;

namespace IobBackupAnalyzer.Avalonia.Views;

/// <summary>
/// Suche über Datenpunkt-ID und Name, darunter der vollständige Wert zum Kopieren.
/// Die gesamte Logik steht in <see cref="DatapointPresenter"/> und ist mit den anderen
/// beiden Oberflächen geteilt.
/// </summary>
public partial class DatapointsView : UserControl
{
    private readonly TextBox _filter;
    private readonly TextBlock _count;
    private readonly DataGrid _rows;
    private readonly TextBlock _placeholder;

    private readonly TextBlock _definition;
    private readonly TextBlock _valueInfo;
    private readonly TextBox _value;
    private readonly Button _copy;

    private List<DatapointHit> _all = new();
    private List<DatapointHit> _filtered = new();

    public DatapointsView()
    {
        AvaloniaXamlLoader.Load(this);
        TableLayout.FillLastColumn(this);

        _filter = this.FindControl<TextBox>("Filter")!;
        _count = this.FindControl<TextBlock>("Count")!;
        _rows = this.FindControl<DataGrid>("Rows")!;
        _placeholder = this.FindControl<TextBlock>("PlaceholderText")!;
        _definition = this.FindControl<TextBlock>("Definition")!;
        _valueInfo = this.FindControl<TextBlock>("ValueInfo")!;
        _value = this.FindControl<TextBox>("Value")!;
        _copy = this.FindControl<Button>("Copy")!;

        _filter.TextChanged += (_, _) => Fill();
        _rows.SelectionChanged += (_, _) => ShowSelected();
        _copy.Click += async (_, _) => await CopyAsync();

        this.FindControl<Button>("Csv")!.Click += async (_, _) => await Dialogs.SaveCsvAsync(
            this, "datenpunkte.csv", DatapointPresenter.CsvColumns,
            DatapointPresenter.Filter(_all, _filter.Text).Select(DatapointPresenter.Row).ToList());

        SetData(null);
    }

    public void SetData(BackupData? data)
    {
        _all = DatapointPresenter.Build(data);

        if (_all.Count == 0)
        {
            _filtered = new List<DatapointHit>();
            _rows.ItemsSource = null;
            _count.Text = "";
            _placeholder.Text = data is null
                ? "Kein Backup geladen.\n\nBitte oben eine Datei öffnen oder hineinziehen."
                : "Die geladene Datei enthält keine Datenpunkte.\n\n" +
                  "Werte stehen nur in einem vollständigen Backitup-Archiv.";
            ShowPlaceholder(true);
            ShowSelected();
            return;
        }

        ShowPlaceholder(false);
        Fill();
    }

    private void ShowPlaceholder(bool show)
    {
        _placeholder.IsVisible = show;
        _rows.IsVisible = !show;
    }

    private void Fill()
    {
        if (_all.Count == 0) return;

        var gefiltert = DatapointPresenter.Filter(_all, _filter.Text);
        _filtered = gefiltert.Take(DatapointPresenter.DisplayLimit).ToList();

        _rows.ItemsSource = _filtered;
        _count.Text = DatapointPresenter.Count(gefiltert.Count, _all.Count,
                                               (_filter.Text ?? "").Trim().Length > 0);
        ShowSelected();
    }

    private void ShowSelected()
    {
        var hit = _rows.SelectedItem as DatapointHit;

        _definition.Text = DatapointPresenter.Definition(hit);
        _valueInfo.Text = DatapointPresenter.ValueInfo(hit);
        _value.Text = DatapointPresenter.FullValue(hit);
        _copy.IsEnabled = !string.IsNullOrEmpty(_value.Text);
    }

    private async Task CopyAsync()
    {
        var text = _value.Text;
        if (string.IsNullOrEmpty(text)) return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            await Dialogs.MessageAsync(this, "Hinweis",
                "Die Zwischenablage steht auf diesem System nicht zur Verfügung.");
            return;
        }

        await clipboard.SetTextAsync(text);
    }
}

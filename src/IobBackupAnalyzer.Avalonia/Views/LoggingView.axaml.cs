using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using IobBackupAnalyzer.Core;

namespace IobBackupAnalyzer.Avalonia.Views;

/// <summary>
/// Logging-Übersicht: welcher Datenpunkt wird von welcher Instanz geloggt und mit welchen
/// Optionen. Bereichswahl, Filter und Spalten kommen aus <see cref="LoggingPresenter"/>.
/// </summary>
public partial class LoggingView : UserControl
{
    private readonly TextBlock _summary;
    private readonly TextBox _filter;
    private readonly ComboBox _scope;
    private readonly TextBlock _count;
    private readonly Button _csv;
    private readonly DataGrid _rows;
    private readonly TextBlock _placeholder;

    private BackupData? _data;
    private List<LoggingRow> _all = new();

    public LoggingView()
    {
        AvaloniaXamlLoader.Load(this);
        TableLayout.FillLastColumn(this);

        _summary = this.FindControl<TextBlock>("Summary")!;
        _filter = this.FindControl<TextBox>("Filter")!;
        _scope = this.FindControl<ComboBox>("Scope")!;
        _count = this.FindControl<TextBlock>("Count")!;
        _csv = this.FindControl<Button>("Csv")!;
        _rows = this.FindControl<DataGrid>("Rows")!;
        _placeholder = this.FindControl<TextBlock>("PlaceholderText")!;

        _scope.ItemsSource = LoggingPresenter.ScopeLabels;
        _scope.SelectedIndex = 0;

        _filter.TextChanged += (_, _) => Fill();
        _scope.SelectionChanged += (_, _) => Fill();
        _csv.Click += async (_, _) => await Dialogs.SaveCsvAsync(this, "logging-uebersicht.csv",
            LoggingPresenter.CsvColumns, CurrentRows().Select(LoggingPresenter.Row).ToList());

        _rows.LoadingRow += (_, e) =>
        {
            var enabled = (e.Row.DataContext as LoggingRow)?.Enabled ?? true;
            if (enabled) e.Row.Classes.Remove("deaktiviert");
            else if (!e.Row.Classes.Contains("deaktiviert")) e.Row.Classes.Add("deaktiviert");
        };

        SetData(null);
    }

    public void SetData(BackupData? data)
    {
        _data = data;

        if (data is null || data.Kind != BackupKind.Full)
        {
            _all = new List<LoggingRow>();
            _summary.Text = "";
            _count.Text = "";
            _rows.ItemsSource = null;
            _placeholder.Text = data is null
                ? "Kein Backup geladen.\n\nBitte oben eine Datei öffnen oder hineinziehen."
                : "Für die Logging-Übersicht wird ein Voll-Backup benötigt.\n\n" +
                  "Die geladene Datei enthält nur Skripte.";
            ShowPlaceholder(true);
            return;
        }

        _all = LoggingAnalyzer.Analyze(data);
        _summary.Text = LoggingPresenter.SummaryText(_all);
        ShowPlaceholder(false);
        Fill();
    }

    private void ShowPlaceholder(bool show)
    {
        _placeholder.IsVisible = show;
        _rows.IsVisible = !show;
    }

    /// <summary>
    /// Die aktuell sichtbare Menge — Grundlage für Liste und CSV-Export gleichermaßen,
    /// damit exportiert wird, was man sieht.
    /// </summary>
    private List<LoggingRow> CurrentRows() =>
        LoggingPresenter.Filter(_all, (LoggingScope)Math.Max(0, _scope.SelectedIndex), _filter.Text);

    private void Fill()
    {
        if (_data is null || _data.Kind != BackupKind.Full) return;

        var rows = CurrentRows();
        _rows.ItemsSource = rows;
        _count.Text = LoggingPresenter.CountText(rows.Count, _all.Count);
    }
}

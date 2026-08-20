using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using IobBackupAnalyzer.Core;

namespace IobBackupAnalyzer.Avalonia.Views;

/// <summary>
/// Auflistung der in VIS verwendeten Datenpunkte, getrennt nach VIS 1 (vis.0) und
/// VIS 2 (vis-2.0), mit den Fundstellen des gewählten Datenpunkts darunter.
/// Zusammenfassung, Filter, Sortierung und Spalten kommen aus <see cref="VisPresenter"/>.
/// </summary>
public partial class VisView : UserControl
{
    private readonly TextBlock _summary;
    private readonly TextBox _filter;
    private readonly ComboBox _scope;
    private readonly TextBlock _count;
    private readonly Button _csv;
    private readonly DataGrid _points;
    private readonly DataGrid _usages;
    private readonly TextBlock _usageHeader;
    private readonly TextBlock _placeholder;
    private readonly Grid _split;

    private BackupData? _data;
    private List<VisDatapoint> _all = new();
    private List<VisDatapoint> _filtered = new();

    public VisView()
    {
        AvaloniaXamlLoader.Load(this);
        TableLayout.FillLastColumn(this);

        _summary = this.FindControl<TextBlock>("Summary")!;
        _filter = this.FindControl<TextBox>("Filter")!;
        _scope = this.FindControl<ComboBox>("Scope")!;
        _count = this.FindControl<TextBlock>("Count")!;
        _csv = this.FindControl<Button>("Csv")!;
        _points = this.FindControl<DataGrid>("Points")!;
        _usages = this.FindControl<DataGrid>("Usages")!;
        _usageHeader = this.FindControl<TextBlock>("UsageHeader")!;
        _placeholder = this.FindControl<TextBlock>("PlaceholderText")!;
        _split = this.FindControl<Grid>("Split")!;

        _scope.ItemsSource = VisPresenter.ScopeLabels;
        _scope.SelectedIndex = 0;

        _filter.TextChanged += (_, _) => ApplyFilter();
        _scope.SelectionChanged += (_, _) => ApplyFilter();
        _points.SelectionChanged += (_, _) => ShowUsages();
        _csv.Click += async (_, _) => await Dialogs.SaveCsvAsync(this, "vis-datenpunkte.csv",
            VisPresenter.CsvColumns, VisPresenter.CsvRows(_filtered));

        _points.LoadingRow += (_, e) =>
        {
            var d = e.Row.DataContext as VisDatapoint;
            var broken = d is not null && (!d.ExistsInBackup || d.AliasTargetMissing);
            if (broken) { if (!e.Row.Classes.Contains("kaputt")) e.Row.Classes.Add("kaputt"); }
            else e.Row.Classes.Remove("kaputt");
        };

        SetData(null);
    }

    public void SetData(BackupData? data)
    {
        _data = data;

        if (data is null || data.Kind != BackupKind.Full)
        {
            _all = new List<VisDatapoint>();
            _filtered = new List<VisDatapoint>();
            _summary.Text = "";
            _count.Text = "";
            _points.ItemsSource = null;
            _usages.ItemsSource = null;
            _placeholder.Text = data is null
                ? "Kein Backup geladen.\n\nBitte oben eine Datei öffnen oder hineinziehen."
                : "Für die VIS-Auswertung wird ein Voll-Backup benötigt.\n\n" +
                  "Die geladene Datei enthält nur Skripte.";
            ShowPlaceholder(true);
            return;
        }

        _all = VisAnalyzer.Analyze(data);
        _summary.Text = VisPresenter.SummaryText(_all, data);

        // Ohne VIS-Views bleibt nur der erklärende Satz in der Zusammenfassung stehen.
        if (data.VisViews.Count == 0)
        {
            _filtered = new List<VisDatapoint>();
            _count.Text = "";
            _points.ItemsSource = null;
            _usages.ItemsSource = null;
            _placeholder.Text = "Dieses Backup enthält keine VIS-Views.";
            ShowPlaceholder(true);
            return;
        }

        ShowPlaceholder(false);
        ApplyFilter();
    }

    private void ShowPlaceholder(bool show)
    {
        _placeholder.IsVisible = show;
        _split.IsVisible = !show;
    }

    private void ApplyFilter()
    {
        if (_data is null || _data.Kind != BackupKind.Full) return;

        _filtered = VisPresenter.Filter(_all, (VisScope)Math.Max(0, _scope.SelectedIndex), _filter.Text);
        _points.ItemsSource = _filtered;
        _count.Text = VisPresenter.CountText(_filtered.Count, _all.Count);

        ShowUsages();
    }

    /// <summary>Listet jede einzelne Fundstelle des gewählten Datenpunkts auf.</summary>
    private void ShowUsages()
    {
        var d = _points.SelectedItem as VisDatapoint;

        // Strg+C ist in Avalonias DataGrid eingebaut; der Hinweis auf den Rechtsklick
        // der WinForms-Fassung entfällt hier deshalb.
        _usageHeader.Text = VisPresenter.UsageHeader(d, "Strg+C kopiert die Auswahl");
        _usages.ItemsSource = d is null ? null : VisPresenter.SortedUsages(d);
    }
}

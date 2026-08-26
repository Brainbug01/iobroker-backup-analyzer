using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
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
    private readonly TextBlock _zipIntro;
    private readonly ComboBox _project;
    private readonly CheckBox _assets;
    private readonly Button _zip;
    private readonly DataGrid _points;
    private readonly DataGrid _usages;
    private readonly TextBlock _usageHeader;
    private readonly TextBlock _placeholder;
    private readonly Grid _split;

    private readonly DataGrid _setRows;
    private readonly TextBlock _setCount;
    private List<WidgetSetRow> _sets = new();

    private BackupData? _data;
    private List<VisDatapoint> _all = new();
    private List<VisDatapoint> _filtered = new();
    private List<VisProjectExporter.VisProject> _projects = new();

    public VisView()
    {
        AvaloniaXamlLoader.Load(this);
        TableLayout.FillLastColumn(this);

        _summary = this.FindControl<TextBlock>("Summary")!;
        _filter = this.FindControl<TextBox>("Filter")!;
        _scope = this.FindControl<ComboBox>("Scope")!;
        _count = this.FindControl<TextBlock>("Count")!;
        _csv = this.FindControl<Button>("Csv")!;
        _zipIntro = this.FindControl<TextBlock>("ZipIntro")!;
        _project = this.FindControl<ComboBox>("Project")!;
        _assets = this.FindControl<CheckBox>("Assets")!;
        _zip = this.FindControl<Button>("Zip")!;
        _points = this.FindControl<DataGrid>("Points")!;

        _setRows = this.FindControl<DataGrid>("SetRows")!;
        _setCount = this.FindControl<TextBlock>("SetCount")!;
        this.FindControl<TextBlock>("SetWarning")!.Text = WidgetSetAnalyzer.Warning;
        this.FindControl<Button>("SetCsv")!.Click += async (_, _) => await Dialogs.SaveCsvAsync(
            this, "widget-saetze.csv", VisPresenter.WidgetSetCsvColumns,
            _sets.Select(VisPresenter.WidgetSetCsvRow).ToList());
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

        _assets.Content = VisPresenter.ZipAssetsLabel;
        _zip.Content = VisPresenter.ZipButtonLabel;
        _project.SelectionChanged += (_, _) => UpdateZipState();
        _zip.Click += async (_, _) => await ExportZipAsync();

        _points.LoadingRow += (_, e) =>
        {
            var d = e.Row.DataContext as VisDatapoint;
            var broken = d is not null && (!d.ExistsInBackup || d.AliasTargetMissing);
            if (broken) { if (!e.Row.Classes.Contains("kaputt")) e.Row.Classes.Add("kaputt"); }
            else e.Row.Classes.Remove("kaputt");
        };

        SetData(null);
    }

    public void SetData(BackupData? data, AnalysisResults? fertig = null)
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
            _sets = new List<WidgetSetRow>();
            _setRows.ItemsSource = null;
            _setCount.Text = "";
            _placeholder.Text = data is null
                ? "Kein Backup geladen.\n\nBitte oben eine Datei öffnen oder hineinziehen."
                : "Für die VIS-Auswertung wird ein Voll-Backup benötigt.\n\n" +
                  "Die geladene Datei enthält nur Skripte.";
            FillProjects();
            ShowPlaceholder(true);
            return;
        }

        _all = fertig?.Vis ?? VisAnalyzer.Analyze(data);
        _summary.Text = VisPresenter.SummaryText(_all, data);
        FillProjects();

        _sets = WidgetSetAnalyzer.Analyze(data);
        _setRows.ItemsSource = _sets;
        _setCount.Text = VisPresenter.WidgetSetCount(_sets);

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

    // ------------------------------------------------- Projekt als ZIP für den Import

    /// <summary>Füllt die Projektauswahl aus dem Dateibaum des Backups.</summary>
    private void FillProjects()
    {
        _projects = _data is null
            ? new List<VisProjectExporter.VisProject>()
            : VisProjectExporter.FindProjects(_data);

        // Die Auswahl zeigt Zeichenketten statt der Records: So ist die Beschriftung in
        // beiden Oberflächen dieselbe, ohne dass hier eine Datenvorlage nötig wäre.
        _project.ItemsSource = _projects.Select(p => p.Label).ToList();
        if (_projects.Count > 0) _project.SelectedIndex = 0;

        _zipIntro.Text = VisPresenter.ZipIntro(_projects.Count);
        UpdateZipState();
    }

    /// <summary>Das gewählte Projekt — null, solange nichts (oder nichts Gültiges) ausgewählt ist.</summary>
    private VisProjectExporter.VisProject? SelectedProject =>
        _project.SelectedIndex >= 0 && _project.SelectedIndex < _projects.Count
            ? _projects[_project.SelectedIndex]
            : null;

    private void UpdateZipState()
    {
        var project = SelectedProject;

        _project.IsEnabled = _projects.Count > 0;
        _zip.IsEnabled = project is not null;
        // Ohne Beiwerk gibt es nichts zum Mitnehmen — der Schalter bliebe eine leere Zusage.
        _assets.IsEnabled = project is { } p && p.Assets.Count > 0;
    }

    /// <summary>
    /// Schreibt das gewählte VIS-Projekt als ZIP, wie sie der Projektimport von VIS
    /// erwartet: der Inhalt des Projektordners flach in der Wurzel.
    /// </summary>
    private async Task ExportZipAsync()
    {
        if (_data is null || SelectedProject is not { } project) return;

        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "VIS-Projekt als ZIP speichern",
            SuggestedFileName = project.SuggestedFileName(_data.CreatedAt),
            DefaultExtension = "zip",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("ZIP-Datei") { Patterns = new[] { "*.zip" } }
            }
        });

        var path = file?.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            var assets = _assets.IsChecked == true;
            var result = await Task.Run(
                () => VisProjectExporter.Export(_data, project, path, assets));

            await Dialogs.MessageAsync(this, "VIS-Projekt exportiert",
                VisPresenter.ZipSummary(_data, project, result));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or NotSupportedException)
        {
            await Dialogs.MessageAsync(this, "Fehler",
                "Der Export ist fehlgeschlagen:\n\n" + ex.Message);
        }
    }
}

using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using IobBackupAnalyzer.Core;

namespace IobBackupAnalyzer.Avalonia.Views;

/// <summary>
/// Säule 1: Backup-Inventar — die Avalonia-Fassung des Übersicht-Tabs.
///
/// Alle Zahlen, Texte, Filter- und Sortierregeln kommen aus
/// <see cref="OverviewPresenter"/>, den sich diese Ansicht mit der WinForms-App teilt.
/// Hier steht ausschließlich Anzeige-Code.
/// </summary>
public partial class OverviewView : UserControl
{
    private readonly TextBlock _header;
    private readonly TextBlock _metrics;
    private readonly Border _limitWarningBox;
    private readonly TextBlock _limitWarning;
    private readonly TextBox _filter;
    private readonly TextBlock _count;
    private readonly Button _csv;
    private readonly DataGrid _instances;
    private readonly TextBlock _placeholder;

    private readonly TextBlock _noInstanceHeader;
    private readonly TextBlock _noInstanceHint;
    private readonly Button _noInstanceCsv;
    private readonly DataGrid _noInstance;
    private readonly Grid _noInstanceSection;

    private BackupData? _data;
    private List<AdapterInstance> _filtered = new();
    private List<AdapterWithoutInstance> _adaptersWithoutInstance = new();

    public OverviewView()
    {
        AvaloniaXamlLoader.Load(this);
        TableLayout.FillLastColumn(this);

        _header = this.FindControl<TextBlock>("Header")!;
        _metrics = this.FindControl<TextBlock>("Metrics")!;
        _limitWarningBox = this.FindControl<Border>("LimitWarningBox")!;
        _limitWarning = this.FindControl<TextBlock>("LimitWarning")!;
        _filter = this.FindControl<TextBox>("Filter")!;
        _count = this.FindControl<TextBlock>("Count")!;
        _csv = this.FindControl<Button>("Csv")!;
        _instances = this.FindControl<DataGrid>("Instances")!;
        _placeholder = this.FindControl<TextBlock>("PlaceholderText")!;

        _noInstanceHeader = this.FindControl<TextBlock>("NoInstanceHeader")!;
        _noInstanceHint = this.FindControl<TextBlock>("NoInstanceHint")!;
        _noInstanceCsv = this.FindControl<Button>("NoInstanceCsv")!;
        _noInstance = this.FindControl<DataGrid>("NoInstance")!;
        _noInstanceSection = this.FindControl<Grid>("NoInstanceSection")!;

        _noInstanceHint.Text = OverviewPresenter.NoInstanceHint;
        ToolTip.SetTip(_limitWarningBox, OverviewPresenter.ObjectLimitHint);

        _filter.TextChanged += (_, _) => ApplyFilter();
        _csv.Click += async (_, _) => await ExportInstancesAsync();
        _noInstanceCsv.Click += async (_, _) => await ExportNoInstanceAsync();

        // Deaktivierte Instanzen gedämpft zeichnen. Avalonia kennt keine datenabhängigen
        // Trigger wie WPF; die Stilklasse wird deshalb beim Aufbau der Zeile gesetzt.
        _instances.LoadingRow += (_, e) =>
        {
            var instance = e.Row.DataContext as AdapterInstance;

            var enabled = instance?.Enabled ?? true;
            if (enabled) e.Row.Classes.Remove("deaktiviert");
            else if (!e.Row.Classes.Contains("deaktiviert")) e.Row.Classes.Add("deaktiviert");

            // Mehr Objekte als das Limit erlaubt — der Stil färbt daraufhin allein die
            // Spalte „Objekte" ein, nicht die ganze Zeile.
            var over = instance?.OverObjectLimit ?? false;
            if (!over) e.Row.Classes.Remove("ueberlimit");
            else if (!e.Row.Classes.Contains("ueberlimit")) e.Row.Classes.Add("ueberlimit");
        };

        SetData(null);
    }

    /// <summary>
    /// Übernimmt ein geladenes Backup — oder <c>null</c>, wenn keines (mehr) vorliegt.
    /// </summary>
    public void SetData(BackupData? data)
    {
        _data = data;

        if (data is null || data.Kind != BackupKind.Full)
        {
            _header.Text = "";
            _metrics.Text = "";
            _limitWarning.Text = "";
            _limitWarningBox.IsVisible = false;
            _count.Text = "";
            _filtered = new List<AdapterInstance>();
            _adaptersWithoutInstance = new List<AdapterWithoutInstance>();
            _instances.ItemsSource = null;
            _noInstance.ItemsSource = null;

            // Ohne Daten wäre „keine — jeder Adapter hat mindestens eine Instanz" eine
            // Aussage über einen Bestand, den es gar nicht gibt. Der ganze untere Bereich
            // bleibt deshalb ausgeblendet, statt eine leere Tabelle zu behaupten.
            _noInstanceSection.IsVisible = false;

            _placeholder.Text = data is null
                ? "Kein Backup geladen.\n\nBitte oben eine Datei öffnen oder hineinziehen."
                : "Für die Übersicht wird ein Voll-Backup benötigt.\n\n" +
                  "Die geladene Datei enthält nur Skripte —\n" +
                  "verfügbar ist damit nur der Tab „Skripte\".";
            ShowPlaceholder(true);
            return;
        }

        _header.Text = OverviewPresenter.HeaderText(data);
        _metrics.Text = OverviewPresenter.MetricsText(data);

        var warning = OverviewPresenter.ObjectLimitWarning(data);
        _limitWarning.Text = warning ?? "";
        _limitWarningBox.IsVisible = warning is not null;

        _adaptersWithoutInstance = OrphanAnalyzer.FindAdaptersWithoutInstance(data);
        _noInstance.ItemsSource = _adaptersWithoutInstance;
        _noInstanceHeader.Text = OverviewPresenter.NoInstanceHeader(_adaptersWithoutInstance.Count);
        _noInstanceSection.IsVisible = true;

        ShowPlaceholder(false);
        ApplyFilter();
    }

    private void ShowPlaceholder(bool show)
    {
        _placeholder.IsVisible = show;
        _instances.IsVisible = !show;
    }

    private void ApplyFilter()
    {
        if (_data is null || _data.Kind != BackupKind.Full) return;

        _filtered = OverviewPresenter.Sort(
            OverviewPresenter.Filter(_data, _filter.Text), column: -1, ascending: true);

        _instances.ItemsSource = _filtered;
        _count.Text = OverviewPresenter.CountText(_filtered.Count, _data.Instances.Count);
    }

    // ------------------------------------------------------------------ CSV-Export

    private Task ExportInstancesAsync() =>
        Dialogs.SaveCsvAsync(this, "adapter-instanzen.csv", OverviewPresenter.InstanceColumns,
                             _filtered.Select(OverviewPresenter.Row).ToList());

    private Task ExportNoInstanceAsync() =>
        Dialogs.SaveCsvAsync(this, "adapter-ohne-instanz.csv", OverviewPresenter.NoInstanceColumns,
                             _adaptersWithoutInstance.Select(OverviewPresenter.Row).ToList());
}

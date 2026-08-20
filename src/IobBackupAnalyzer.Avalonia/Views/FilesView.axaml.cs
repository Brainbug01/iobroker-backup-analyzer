using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using IobBackupAnalyzer.Core;

namespace IobBackupAnalyzer.Avalonia.Views;

/// <summary>
/// Die Dateien aus dem files/-Baum des Backups — der Admin-Dateibereich. Liste mit Filter
/// und Export; Zusammenfassung, Filter und Spalten kommen aus <see cref="FilesPresenter"/>.
/// </summary>
public partial class FilesView : UserControl
{
    private readonly TextBlock _summary;
    private readonly TextBox _filter;
    private readonly ComboBox _namespaces;
    private readonly TextBlock _count;
    private readonly DataGrid _rows;
    private readonly Button _exportAll;
    private readonly TextBlock _placeholder;

    private BackupData? _data;
    private List<BackupFileInfo> _filtered = new();

    public FilesView()
    {
        AvaloniaXamlLoader.Load(this);
        TableLayout.FillLastColumn(this);

        _summary = this.FindControl<TextBlock>("Summary")!;
        _filter = this.FindControl<TextBox>("Filter")!;
        _namespaces = this.FindControl<ComboBox>("Namespaces")!;
        _count = this.FindControl<TextBlock>("Count")!;
        _rows = this.FindControl<DataGrid>("Rows")!;
        _placeholder = this.FindControl<TextBlock>("PlaceholderText")!;

        _filter.TextChanged += (_, _) => Fill();
        _namespaces.SelectionChanged += (_, _) => Fill();

        this.FindControl<Button>("Csv")!.Click += async (_, _) => await Dialogs.SaveCsvAsync(
            this, "dateien.csv", FilesPresenter.Columns,
            _filtered.Select(FilesPresenter.Row).ToList());
        _exportAll = this.FindControl<Button>("ExportAll")!;

        this.FindControl<Button>("ExportSelected")!.Click += async (_, _) => await ExportAsync(true);
        _exportAll.Click += async (_, _) => await ExportAsync(false);

        SetData(null);
    }

    public void SetData(BackupData? data)
    {
        _data = data;

        if (data is null || data.Files.Count == 0)
        {
            _filtered = new List<BackupFileInfo>();
            _rows.ItemsSource = null;
            _summary.Text = "";
            _count.Text = "";
            _placeholder.Text = data is null
                ? "Kein Backup geladen.\n\nBitte oben eine Datei öffnen oder hineinziehen."
                : "Die geladene Datei enthält keinen Dateibereich.\n\n" +
                  "Dateien liegen nur in einem vollständigen Backitup-Archiv.";
            ShowPlaceholder(true);
            return;
        }

        _summary.Text = FilesPresenter.SummaryText(data);

        _namespaces.ItemsSource = FilesPresenter.NamespaceChoices(data.Files);
        _namespaces.SelectedIndex = 0;

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
        if (_data is null || _data.Files.Count == 0) return;

        _filtered = FilesPresenter.Filter(_data.Files, _namespaces.SelectedItem as string, _filter.Text);
        _rows.ItemsSource = _filtered;
        _count.Text = FilesPresenter.CountText(_filtered.Count, _data.Files.Count);
        _exportAll.Content = FilesPresenter.ExportAllLabel(_filtered.Count, _data.Files.Count);
    }

    private async Task ExportAsync(bool selectedOnly)
    {
        if (_data is null || _data.Files.Count == 0) return;

        // Bei „Alle" bewusst die gefilterte Liste: Wer nach einem Namensraum filtert,
        // erwartet auch nur diesen im Export.
        var toExport = selectedOnly
            ? _rows.SelectedItems.Cast<object>().OfType<BackupFileInfo>().ToList()
            : _filtered;

        if (selectedOnly && toExport.Count == 0)
        {
            await Dialogs.MessageAsync(this, "Hinweis",
                "Bitte zuerst mindestens eine Datei in der Liste auswählen.");
            return;
        }

        if (toExport.Count == 0) return;

        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = $"Zielordner für {toExport.Count} Datei(en) wählen",
            AllowMultiple = false
        });

        var target = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        if (string.IsNullOrEmpty(target)) return;

        try
        {
            var result = await Task.Run(() => BackupFileExporter.Export(_data, toExport, target));
            await Dialogs.MessageAsync(this, "Export abgeschlossen",
                FilesPresenter.ExportSummary(result));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await Dialogs.MessageAsync(this, "Fehler",
                "Der Export ist fehlgeschlagen:\n\n" + ex.Message);
        }
    }
}

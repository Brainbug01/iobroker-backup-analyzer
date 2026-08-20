using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using IobBackupAnalyzer.Core;

namespace IobBackupAnalyzer.Avalonia.Views;

/// <summary>
/// Vergleich zweier Backups. Das erste ist das im Hauptfenster geladene, das zweite wird
/// hier gewählt oder hineingezogen. Welches „vorher" ist, entscheidet der Backup-Zeitpunkt.
/// Texte, Filter und Zeilenaufbereitung kommen aus <see cref="ComparePresenter"/>.
/// </summary>
public partial class CompareView : UserControl
{
    /// <summary>Eine Zeile der Objekt-ID-Liste.</summary>
    private sealed record ObjectIdRow(string Change, string Id, bool IsAdded);

    private readonly TextBlock _loadedInfo;
    private readonly TextBlock _summary;
    private readonly TextBlock _systemInfo;
    private readonly TextBlock _placeholder;
    private readonly ProgressBar _progress;
    private readonly Button _pick;
    private readonly Button _reset;
    private readonly TabControl _sub;

    private readonly DataGrid _metrics;
    private readonly DataGrid _instances;
    private readonly DataGrid _scripts;
    private readonly DataGrid _diff;
    private readonly DataGrid _namespaces;
    private readonly DataGrid _objectIds;
    private readonly DataGrid _views;

    private readonly CheckBox _onlyChangedInstances;
    private readonly CheckBox _onlyChangedScripts;
    private readonly CheckBox _onlyChangedLines;
    private readonly CheckBox _onlyChangedViews;
    private readonly TextBlock _diffInfo;

    private BackupData? _loaded;
    private BackupComparison? _cmp;
    private CancellationTokenSource? _cts;

    public CompareView()
    {
        AvaloniaXamlLoader.Load(this);
        TableLayout.FillLastColumn(this);

        _loadedInfo = this.FindControl<TextBlock>("LoadedInfo")!;
        _summary = this.FindControl<TextBlock>("Summary")!;
        _systemInfo = this.FindControl<TextBlock>("SystemInfo")!;
        _placeholder = this.FindControl<TextBlock>("PlaceholderText")!;
        _progress = this.FindControl<ProgressBar>("Progress")!;
        _pick = this.FindControl<Button>("Pick")!;
        _reset = this.FindControl<Button>("ResetButton")!;
        _sub = this.FindControl<TabControl>("Sub")!;

        _metrics = this.FindControl<DataGrid>("Metrics")!;
        _instances = this.FindControl<DataGrid>("Instances")!;
        _scripts = this.FindControl<DataGrid>("Scripts")!;
        _diff = this.FindControl<DataGrid>("Diff")!;
        _namespaces = this.FindControl<DataGrid>("Namespaces")!;
        _objectIds = this.FindControl<DataGrid>("ObjectIds")!;
        _views = this.FindControl<DataGrid>("Views")!;

        _onlyChangedInstances = this.FindControl<CheckBox>("OnlyChangedInstances")!;
        _onlyChangedScripts = this.FindControl<CheckBox>("OnlyChangedScripts")!;
        _onlyChangedLines = this.FindControl<CheckBox>("OnlyChangedLines")!;
        _onlyChangedViews = this.FindControl<CheckBox>("OnlyChangedViews")!;
        _diffInfo = this.FindControl<TextBlock>("DiffInfo")!;

        _pick.Click += async (_, _) => await PickAsync();
        _reset.Click += (_, _) => Reset();
        _onlyChangedInstances.IsCheckedChanged += (_, _) => FillInstances();
        _onlyChangedScripts.IsCheckedChanged += (_, _) => FillScripts();
        _onlyChangedLines.IsCheckedChanged += (_, _) => ShowDiff();
        _onlyChangedViews.IsCheckedChanged += (_, _) => FillViews();
        _scripts.SelectionChanged += (_, _) => ShowDiff();
        _namespaces.SelectionChanged += (_, _) => FillObjectIds();

        this.FindControl<Button>("CsvMetrics")!.Click += async (_, _) => await ExportAsync(
            "vergleich-kennzahlen.csv", ComparePresenter.MetricColumns,
            _cmp?.Metrics.Select(ComparePresenter.Row).ToList());
        this.FindControl<Button>("CsvInstances")!.Click += async (_, _) => await ExportAsync(
            "vergleich-instanzen.csv", ComparePresenter.InstanceCsvColumns,
            _cmp is null ? null
                : ComparePresenter.FilterInstances(_cmp, _onlyChangedInstances.IsChecked == true)
                                  .Select(ComparePresenter.Row).ToList());
        this.FindControl<Button>("CsvScripts")!.Click += async (_, _) => await ExportAsync(
            "vergleich-skripte.csv", ComparePresenter.ScriptCsvColumns,
            _cmp is null ? null
                : ComparePresenter.FilterScripts(_cmp, _onlyChangedScripts.IsChecked == true)
                                  .Select(ComparePresenter.Row).ToList());
        this.FindControl<Button>("CsvObjects")!.Click += async (_, _) => await ExportAsync(
            "vergleich-objekte.csv", ComparePresenter.ObjectCsvColumns,
            _cmp is null ? null : ComparePresenter.ObjectCsvRows(_cmp));
        this.FindControl<Button>("CsvViews")!.Click += async (_, _) => await ExportAsync(
            "vergleich-views.csv", ComparePresenter.ViewCsvColumns,
            _cmp is null ? null
                : ComparePresenter.FilterViews(_cmp, _onlyChangedViews.IsChecked == true)
                                  .Select(ComparePresenter.Row).ToList());

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        _metrics.LoadingRow += (_, e) => SetEmphasis(e.Row,
            e.Row.DataContext is MetricRow m ? ComparePresenter.Emphasis(m) : RowEmphasis.None);
        _instances.LoadingRow += (_, e) => SetEmphasis(e.Row,
            e.Row.DataContext is InstanceChange i ? ComparePresenter.Emphasis(i) : RowEmphasis.None);
        _scripts.LoadingRow += (_, e) => SetEmphasis(e.Row,
            e.Row.DataContext is ScriptChange s ? ComparePresenter.Emphasis(s) : RowEmphasis.None);
        _namespaces.LoadingRow += (_, e) => SetEmphasis(e.Row,
            e.Row.DataContext is NamespaceChange n ? ComparePresenter.Emphasis(n) : RowEmphasis.None);
        _views.LoadingRow += (_, e) => SetEmphasis(e.Row,
            e.Row.DataContext is ViewChange v ? ComparePresenter.Emphasis(v) : RowEmphasis.None);
        _objectIds.LoadingRow += (_, e) => SetEmphasis(e.Row,
            (e.Row.DataContext as ObjectIdRow)?.IsAdded == true ? RowEmphasis.Positive : RowEmphasis.Problem);

        _diff.LoadingRow += (_, e) =>
        {
            var l = e.Row.DataContext as DiffDisplayLine;
            Apply(e.Row, "luecke", l?.IsGap == true);
            Apply(e.Row, "hinzugefuegt", l is { IsGap: false, Kind: DiffKind.Added });
            Apply(e.Row, "entfernt", l is { IsGap: false, Kind: DiffKind.Removed });
        };

        SetData(null);
    }

    /// <summary>
    /// Setzt die Stilklasse zur Einstufung. Zeilen werden beim Scrollen wiederverwendet —
    /// nicht zutreffende Klassen müssen deshalb aktiv entfernt werden.
    /// </summary>
    private static void SetEmphasis(DataGridRow row, RowEmphasis emphasis)
    {
        Apply(row, "gedaempft", emphasis == RowEmphasis.Muted);
        Apply(row, "positiv", emphasis == RowEmphasis.Positive);
        Apply(row, "warnung", emphasis == RowEmphasis.Warn);
        Apply(row, "problem", emphasis == RowEmphasis.Problem);
    }

    private static void Apply(DataGridRow row, string name, bool on)
    {
        if (on) { if (!row.Classes.Contains(name)) row.Classes.Add(name); }
        else row.Classes.Remove(name);
    }

    // ------------------------------------------------------------------ Zustand

    public void SetData(BackupData? data)
    {
        _loaded = data;

        // Ein neu geladenes Hauptbackup macht den bisherigen Vergleich ungültig.
        Reset();

        _loadedInfo.Text = data is null ? "Kein Backup geladen." : ComparePresenter.LoadedText(data);
        _pick.IsEnabled = data is not null;
    }

    private void Reset()
    {
        _cmp = null;
        _sub.IsVisible = false;
        _placeholder.IsVisible = true;
        _reset.IsEnabled = false;
        _summary.Text = "";
        _systemInfo.Text = "";
        _placeholder.Text = ComparePresenter.PlaceholderText(_loaded);
    }

    // ------------------------------------------------------------------ Laden

    private void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = _loaded is not null && e.DataTransfer.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (_loaded is null) return;
        var path = e.DataTransfer.TryGetFile()?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path)) await LoadOtherAsync(path);
    }

    private async Task PickAsync()
    {
        if (_loaded is null) return;
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Zweites Backup zum Vergleich auswählen",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("ioBroker-Backups")
                {
                    Patterns = new[] { "*.tar.gz", "*.tgz", "*.json", "*.jsonl" }
                },
                FilePickerFileTypes.All
            }
        });

        var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (!string.IsNullOrEmpty(path)) await LoadOtherAsync(path);
    }

    private async Task LoadOtherAsync(string path)
    {
        if (_loaded is null) return;

        if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(_loaded.SourceFile),
                          StringComparison.OrdinalIgnoreCase))
        {
            await Dialogs.MessageAsync(this, "Hinweis",
                "Das ist dieselbe Datei, die bereits geladen ist.\n\n" +
                "Bitte ein anderes Backup zum Vergleich auswählen.");
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        SetBusy(true);
        try
        {
            var other = await BackupLoader.LoadAsync(path, null, ct);

            // Ein Skript-Backup gegen ein Voll-Backup zu stellen ergäbe lauter
            // Scheinänderungen — jedes Objekt und jede Instanz fehlte auf einer Seite.
            if (other.Kind != _loaded.Kind)
            {
                await Dialogs.MessageAsync(this, "Nicht vergleichbar",
                    ComparePresenter.NotComparableText(_loaded.Kind, other.Kind));
                return;
            }

            // Stammen beide vom selben ioBroker? Ein Unterschied ist kein Fehler, aber er
            // erklärt tausende Scheinänderungen — deshalb nachfragen.
            if (BackupComparer.MatchSystems(_loaded.System, other.System) == SystemMatch.Different)
            {
                var proceed = await Dialogs.ConfirmAsync(this, "Verschiedene Systeme",
                    ComparePresenter.DifferentSystemText(_loaded.System, other.System));
                if (!proceed) return;
            }

            _cmp = await BackupComparer.CompareAsync(_loaded, other, ct);

            FillAll();
            _sub.IsVisible = true;
            _placeholder.IsVisible = false;
            _reset.IsEnabled = true;
        }
        catch (OperationCanceledException)
        {
            // Abbruch durch einen neuen Ladevorgang — nichts zu tun.
        }
        catch (NotABackupException ex)
        {
            await Dialogs.MessageAsync(this, "Backup konnte nicht geladen werden",
                $"Datei: {Path.GetFileName(path)}\n\n{ex.Message}");
        }
        catch (Exception ex)
        {
            await Dialogs.MessageAsync(this, "Fehler",
                "Die Datei konnte nicht geladen werden:\n\n" + ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _progress.IsVisible = busy;
        _pick.IsEnabled = !busy;
        _sub.IsEnabled = !busy;
    }

    // ------------------------------------------------------------------ Anzeige

    private void FillAll()
    {
        if (_cmp is null) return;

        _summary.Text = ComparePresenter.SummaryText(_cmp);
        _systemInfo.Text = _cmp.SystemMatchText;
        _systemInfo.Foreground = new SolidColorBrush(_cmp.SystemMatch switch
        {
            SystemMatch.Same => Color.Parse("#2FA36B"),
            SystemMatch.Different => Color.Parse("#D9534F"),
            _ => Colors.Gray
        });

        _metrics.ItemsSource = _cmp.Metrics;
        FillInstances();
        FillScripts();
        FillNamespaces();
        FillViews();
    }

    private void FillInstances()
    {
        if (_cmp is null) return;
        _instances.ItemsSource = ComparePresenter.FilterInstances(_cmp, _onlyChangedInstances.IsChecked == true);
    }

    private void FillScripts()
    {
        if (_cmp is null) return;

        var rows = ComparePresenter.FilterScripts(_cmp, _onlyChangedScripts.IsChecked == true);
        _scripts.ItemsSource = rows;
        _diff.ItemsSource = null;
        _diffInfo.Text = rows.Count == 0
            ? "Keine Skriptänderungen."
            : "Skript auswählen, um den Vergleich zu sehen.";
    }

    /// <summary>
    /// Zeigt den Zeilenvergleich des ausgewählten Skripts. Bei Blockly wird das XML
    /// verglichen — es ist die eigentliche Quelle, das JavaScript daneben nur erzeugt.
    /// </summary>
    private void ShowDiff()
    {
        if (_scripts.SelectedItem is not ScriptChange sc)
        {
            _diff.ItemsSource = null;
            return;
        }

        if (sc.OnlyStatusChanged)
        {
            _diff.ItemsSource = new[]
            {
                new DiffDisplayLine("", "", "",
                    "Der Inhalt ist unverändert — geändert hat sich nur der Aktiv-Status.",
                    DiffKind.Unchanged, true)
            };
            _diffInfo.Text = ComparePresenter.DiffBasis(sc);
            return;
        }

        var oldText = sc.Before is null ? "" : ScriptChange.ComparableText(sc.Before);
        var newText = sc.After is null ? "" : ScriptChange.ComparableText(sc.After);

        var result = TextDiff.Compare(oldText, newText);
        _diff.ItemsSource = ComparePresenter.VisibleLines(result, _onlyChangedLines.IsChecked == true);
        _diffInfo.Text = ComparePresenter.DiffInfoText(sc, result);
    }

    private void FillNamespaces()
    {
        if (_cmp is null) return;

        _namespaces.ItemsSource = _cmp.Namespaces;
        _objectIds.ItemsSource = null;
        if (_cmp.Namespaces.Count > 0) _namespaces.SelectedIndex = 0;
    }

    private void FillObjectIds()
    {
        if (_namespaces.SelectedItem is not NamespaceChange n)
        {
            _objectIds.ItemsSource = null;
            return;
        }

        _objectIds.ItemsSource = ComparePresenter.ObjectIds(n)
            .Select(r => new ObjectIdRow(r.Change, r.Id, r.IsAdded))
            .ToList();
    }

    private void FillViews()
    {
        if (_cmp is null) return;
        _views.ItemsSource = ComparePresenter.FilterViews(_cmp, _onlyChangedViews.IsChecked == true);
    }

    private async Task ExportAsync(string name, string[] columns, IList<string[]>? rows)
    {
        if (rows is null) return;
        await Dialogs.SaveCsvAsync(this, name, columns, rows);
    }
}

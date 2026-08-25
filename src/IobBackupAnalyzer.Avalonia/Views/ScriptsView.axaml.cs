using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using IobBackupAnalyzer.Core;

namespace IobBackupAnalyzer.Avalonia.Views;

/// <summary>
/// Säule 2: Skript-Extraktion. Liste mit Filtern oben, Vorschau des gewählten Skripts
/// unten; bei Blockly lässt sich zwischen erzeugtem JavaScript und dem XML umschalten.
/// Filter, Sortierung und Vorschautext kommen aus <see cref="ScriptsPresenter"/>.
/// </summary>
public partial class ScriptsView : UserControl
{
    private readonly TextBox _search;
    private readonly ComboBox _searchMode;
    private readonly ComboBox _typeFilter;
    private readonly CheckBox _hideDisabled;
    private readonly CheckBox _onlyWithHints;
    private readonly CheckBox _withGeneratedJs;
    private readonly Button _exportAll;
    private readonly TextBlock _count;
    private readonly DataGrid _list;
    private readonly Border _hintsBox;
    private readonly SelectableTextBlock _hints;
    private readonly TextBox _preview;
    private readonly StackPanel _viewChoice;

    /// <summary>Kopiert den Text, der gerade in der Vorschau steht.</summary>
    private readonly Button _copyPreview;
    private readonly RadioButton _showXml;
    private readonly RadioButton _showJs;
    private readonly TextBlock _placeholder;
    private readonly Grid _split;

    private BackupData? _data;
    private List<ScriptInfo> _filtered = new();
    private ScriptInfo? _current;

    public ScriptsView()
    {
        AvaloniaXamlLoader.Load(this);
        TableLayout.FillLastColumn(this);

        _search = this.FindControl<TextBox>("Search")!;
        _searchMode = this.FindControl<ComboBox>("SearchMode")!;
        _typeFilter = this.FindControl<ComboBox>("TypeFilter")!;
        _hideDisabled = this.FindControl<CheckBox>("HideDisabled")!;
        _onlyWithHints = this.FindControl<CheckBox>("OnlyWithHints")!;
        _withGeneratedJs = this.FindControl<CheckBox>("WithGeneratedJs")!;
        _count = this.FindControl<TextBlock>("Count")!;
        _list = this.FindControl<DataGrid>("List")!;
        _hintsBox = this.FindControl<Border>("HintsBox")!;
        _hints = this.FindControl<SelectableTextBlock>("Hints")!;
        _preview = this.FindControl<TextBox>("Preview")!;
        _viewChoice = this.FindControl<StackPanel>("ViewChoice")!;
        _copyPreview = this.FindControl<Button>("CopyPreview")!;
        _showXml = this.FindControl<RadioButton>("ShowXml")!;
        _showJs = this.FindControl<RadioButton>("ShowJs")!;
        _placeholder = this.FindControl<TextBlock>("PlaceholderText")!;
        _split = this.FindControl<Grid>("Split")!;

        // Beschriftung und Erklärung kommen aus dem Presenter, damit beide Oberflächen
        // dasselbe sagen.
        _withGeneratedJs.Content = ScriptsPresenter.GeneratedJsLabel;
        ToolTip.SetTip(_withGeneratedJs, ScriptsPresenter.GeneratedJsHint);

        _onlyWithHints.Content = ScriptsPresenter.OnlyWithHintsLabel;
        ToolTip.SetTip(_onlyWithHints, ScriptsPresenter.HintsHint);

        _searchMode.ItemsSource = ScriptsPresenter.SearchModeLabels;
        _searchMode.SelectedIndex = 0;
        _typeFilter.ItemsSource = ScriptsPresenter.TypeLabels;
        _typeFilter.SelectedIndex = 0;

        _search.TextChanged += (_, _) => ApplyFilter();
        _searchMode.SelectionChanged += (_, _) => ApplyFilter();
        _typeFilter.SelectionChanged += (_, _) => ApplyFilter();
        _hideDisabled.IsCheckedChanged += (_, _) => ApplyFilter();
        _onlyWithHints.IsCheckedChanged += (_, _) => ApplyFilter();
        _list.SelectionChanged += (_, _) => ShowPreview();
        _showXml.IsCheckedChanged += (_, _) => ShowPreview();
        _showJs.IsCheckedChanged += (_, _) => ShowPreview();

        _exportAll = this.FindControl<Button>("ExportAll")!;

        this.FindControl<Button>("Clipboard")!.Click += async (_, _) => await CopyToClipboardAsync();
        _copyPreview.Click += async (_, _) => await CopyToClipboardAsync();
        this.FindControl<Button>("ExportSelected")!.Click += async (_, _) => await ExportAsync(true);
        _exportAll.Click += async (_, _) => await ExportAsync(false);
        this.FindControl<Button>("ListCsv")!.Click += async (_, _) => await Dialogs.SaveCsvAsync(this,
            "skriptliste.csv", ScriptsPresenter.Columns,
            _filtered.Select(ScriptsPresenter.Row).ToList());

        _list.LoadingRow += (_, e) =>
        {
            var s = e.Row.DataContext as ScriptInfo;
            var emphasis = s is null ? RowEmphasis.None : ScriptsPresenter.Emphasis(s);
            Apply(e.Row, "gedaempft", emphasis == RowEmphasis.Muted);
            Apply(e.Row, "warnung", emphasis == RowEmphasis.Warn);
            Apply(e.Row, "problem", emphasis == RowEmphasis.Problem);
        };

        SetData(null);

        static void Apply(DataGridRow row, string name, bool on)
        {
            if (on) { if (!row.Classes.Contains(name)) row.Classes.Add(name); }
            else row.Classes.Remove(name);
        }
    }

    private ScriptSearchMode CurrentSearchMode =>
        (ScriptSearchMode)Math.Max(0, _searchMode.SelectedIndex);

    public void SetData(BackupData? data)
    {
        _data = data;

        if (data is null)
        {
            _filtered = new List<ScriptInfo>();
            _current = null;
            _list.ItemsSource = null;
            _preview.Text = "";
            _count.Text = "";
            _viewChoice.IsVisible = false;
            _copyPreview.IsEnabled = false;
            _hintsBox.IsVisible = false;
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

    /// <summary>
    /// Wählt ein Skript anhand seiner ioBroker-ID aus — der Anlaufpunkt für den Doppelklick
    /// im Tab „Verwendung".
    ///
    /// Verdeckt die laufende Filterung das Skript (Suchbegriff, Typfilter, ausgeblendete
    /// deaktivierte Skripte), werden die Filter zurückgesetzt: Ein Sprung, der in einer
    /// leeren Liste endet, sähe wie ein Fehler aus.
    /// </summary>
    public bool SelectScript(string scriptId)
    {
        if (_data is null) return false;
        if (_data.Scripts.All(s => s.Id != scriptId)) return false;

        if (_filtered.All(s => s.Id != scriptId))
        {
            _search.Text = "";
            _typeFilter.SelectedIndex = 0;
            _hideDisabled.IsChecked = false;
            _onlyWithHints.IsChecked = false;
            ApplyFilter();
        }

        var target = _filtered.FirstOrDefault(s => s.Id == scriptId);
        if (target is null) return false;

        _list.SelectedItem = target;
        _list.Focus();
        BringIntoView(target);
        return true;
    }

    /// <summary>
    /// Rollt die gewählte Zeile ins Bild — und zwar nachgereicht.
    ///
    /// Der Sprung aus dem Tab „Verwendung" hängt die Tabelle gerade erst wieder ein: Ihre
    /// Zeilen entstehen erst im nächsten Layoutlauf, und ein sofortiges Rollen läuft deshalb
    /// ins Leere — die Zeile war ausgewählt, stand aber weiter unten außerhalb des Bildes.
    /// Darum zusätzlich nach dem Layout (Loaded) und ein letztes Mal, wenn alles andere
    /// erledigt ist (Background); ein überflüssiger Aufruf kostet nichts.
    /// </summary>
    private void BringIntoView(ScriptInfo target)
    {
        _list.ScrollIntoView(target, null);

        // Einmalig am nächsten Layoutlauf: Dann steht die Tabelle in ihrer Größe da und
        // hat ihre Zeilen erzeugt. Der Handler hängt sich sofort wieder ab, sonst würde
        // die Tabelle bei jedem Rollen wieder zur alten Zeile zurückspringen.
        void OnLayoutUpdated(object? sender, EventArgs e)
        {
            _list.LayoutUpdated -= OnLayoutUpdated;
            _list.ScrollIntoView(target, null);
        }

        _list.LayoutUpdated += OnLayoutUpdated;

        // Und noch einmal, wenn alles andere abgearbeitet ist — greift, falls der
        // Layoutlauf schon vorbei war und das Ereignis deshalb nicht mehr kommt.
        Dispatcher.UIThread.Post(
            () => _list.ScrollIntoView(target, null), DispatcherPriority.Background);
    }

    private void ApplyFilter()
    {
        if (_data is null) return;

        _filtered = ScriptsPresenter.Sort(
            ScriptsPresenter.Filter(_data.Scripts, _hideDisabled.IsChecked == true,
                                    _typeFilter.SelectedIndex, CurrentSearchMode, _search.Text,
                                    _onlyWithHints.IsChecked == true),
            column: -1, ascending: true);

        _list.ItemsSource = _filtered;
        _count.Text = ScriptsPresenter.CountText(_filtered.Count, _data, CurrentSearchMode, _search.Text);
        _exportAll.Content = ScriptsPresenter.ExportAllLabel(_filtered.Count, _data.Scripts.Count);

        // Ohne Auswahl bliebe die Vorschau leer — der erste Treffer ist fast immer der,
        // den man sehen will.
        if (_filtered.Count > 0 && _list.SelectedItem is null) _list.SelectedIndex = 0;
        else ShowPreview();
    }

    private void ShowPreview()
    {
        _current = _list.SelectedItem as ScriptInfo;

        var details = ScriptsPresenter.HintDetails(_current);
        _hintsBox.IsVisible = details.Length > 0;
        _hints.Text = details;

        var hasXml = ScriptsPresenter.HasXmlView(_current);
        _viewChoice.IsVisible = hasXml;
        if (!hasXml && _showXml.IsChecked == true) _showJs.IsChecked = true;

        _copyPreview.IsEnabled = true;
        _copyPreview.Content = ScriptsPresenter.CopyLabel(_current, _showXml.IsChecked == true);

        _preview.Text = ScriptsPresenter.PreviewText(_current, _showXml.IsChecked == true);
    }

    private async Task CopyToClipboardAsync()
    {
        if (_current is null)
        {
            await Dialogs.MessageAsync(this, "Hinweis", "Bitte zuerst ein Skript auswählen.");
            return;
        }

        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        if (string.IsNullOrEmpty(_preview.Text)) return;

        await clipboard.SetTextAsync(_preview.Text);
        // Kurze Rückmeldung in der Zählzeile; wird beim nächsten Filtern überschrieben.
        _count.Text = ScriptsPresenter.CopyDoneText(_current, _showXml.IsChecked == true);
    }

    private async Task ExportAsync(bool selectedOnly)
    {
        if (_data is null) return;

        // Bei „Alle" bewusst die gefilterte Liste: Wer nach „Heizung" sucht und dann
        // exportiert, meint die Treffer — nicht den gesamten Bestand. Der Knopf sagt das auch.
        var toExport = selectedOnly
            ? _list.SelectedItems.Cast<object>().OfType<ScriptInfo>().ToList()
            : _filtered;

        if (toExport.Count == 0)
        {
            await Dialogs.MessageAsync(this, "Hinweis", selectedOnly
                ? "Bitte zuerst mindestens ein Skript in der Liste auswählen."
                : "Die aktuelle Filterung enthält kein Skript.");
            return;
        }

        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = $"Zielordner für {toExport.Count} Skript(e) wählen",
            AllowMultiple = false
        });

        var target = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        if (string.IsNullOrEmpty(target)) return;

        try
        {
            var result = ScriptExporter.Export(toExport, target,
                _withGeneratedJs.IsChecked == true
                    ? ScriptExportFormat.WithGeneratedJs
                    : ScriptExportFormat.OriginalOnly,
                _data?.SourceFile);
            await Dialogs.MessageAsync(this, "Export abgeschlossen",
                ScriptsPresenter.ExportSummary(result));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await Dialogs.MessageAsync(this, "Fehler",
                "Der Export ist fehlgeschlagen:\n\n" + ex.Message);
        }
    }
}

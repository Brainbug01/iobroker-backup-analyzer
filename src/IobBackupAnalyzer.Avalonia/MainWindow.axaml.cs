using global::Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using IobBackupAnalyzer.Avalonia.Views;
using IobBackupAnalyzer.Core;

namespace IobBackupAnalyzer.Avalonia;

public partial class MainWindow : Window
{
    private readonly Button _btnOpen;
    private readonly TextBlock _lblFile;
    private readonly TextBlock _lblStatus;
    private readonly ProgressBar _progress;
    private readonly OverviewView _overview;
    private readonly BackupCheckView _backupCheck;
    private readonly VisView _vis;
    private readonly OrphansView _orphans;
    private readonly DatapointsView _datapoints;
    private readonly ScriptsView _scripts;
    private readonly UsageView _usage;
    private readonly CompareView _compare;
    private readonly LoggingView _logging;
    private readonly AliasView _aliases;
    private readonly FilesView _files;

    private readonly Button _btnRecent;

    private BackupData? _data;
    private readonly ComboBox _themeSelect;

    /// <summary>
    /// Eigene Datei je Oberfläche: Ein gemeinsamer Fensterzustand zweier gleichzeitig
    /// laufender Programme wäre eher störend als hilfreich.
    /// </summary>
    private const string SettingsFile = "einstellungen-avalonia.json";

    private readonly UserSettings _settings;

    /// <summary>Beschriftungen der Darstellungswahl, in der Reihenfolge von <see cref="ThemeChoice"/>.</summary>
    private static readonly string[] ThemeLabels = { "System", "Hell", "Dunkel" };

    private CancellationTokenSource? _cts;

    public MainWindow() : this(null) { }

    public MainWindow(string? startFile)
    {
        AvaloniaXamlLoader.Load(this);

        _settings = UserSettings.Load(SettingsFile);

        _btnOpen = this.FindControl<Button>("BtnOpen")!;
        _lblFile = this.FindControl<TextBlock>("LblFile")!;
        _lblStatus = this.FindControl<TextBlock>("LblStatus")!;
        _progress = this.FindControl<ProgressBar>("Progress")!;
        _overview = this.FindControl<OverviewView>("Overview")!;
        _backupCheck = this.FindControl<BackupCheckView>("BackupCheck")!;
        _vis = this.FindControl<VisView>("Vis")!;
        _orphans = this.FindControl<OrphansView>("Orphans")!;
        _datapoints = this.FindControl<DatapointsView>("Datapoints")!;
        _scripts = this.FindControl<ScriptsView>("Scripts")!;
        _usage = this.FindControl<UsageView>("Usage")!;
        _compare = this.FindControl<CompareView>("Compare")!;
        _logging = this.FindControl<LoggingView>("Logging")!;
        _aliases = this.FindControl<AliasView>("Aliases")!;
        _files = this.FindControl<FilesView>("Files")!;
        this.FindControl<ContentControl>("ChangelogHost")!.Content = new HelpView(ChangelogContent.Blocks);

        _btnRecent = this.FindControl<Button>("BtnRecent")!;
        _themeSelect = this.FindControl<ComboBox>("ThemeSelect")!;

        Title = AppInfo.WindowTitle;
        this.FindControl<TextBlock>("LblVersion")!.Text = AppInfo.LongVersion;

        // ---------- gemerkter Zustand ----------
        Width = _settings.WindowWidth;
        Height = _settings.WindowHeight;
        if (_settings.Maximized) WindowState = WindowState.Maximized;

        _btnRecent.IsEnabled = _settings.LastFile is not null && File.Exists(_settings.LastFile);
        _btnRecent.Click += async (_, _) =>
        {
            if (_settings.LastFile is not null) await LoadFileAsync(_settings.LastFile);
        };

        _themeSelect.ItemsSource = ThemeLabels;
        _themeSelect.SelectedIndex = (int)_settings.Theme;
        ApplyTheme(_settings.Theme);
        _themeSelect.SelectionChanged += (_, _) =>
        {
            var choice = (ThemeChoice)Math.Max(0, _themeSelect.SelectedIndex);
            ApplyTheme(choice);
            _settings.Theme = choice;
            _settings.Save(SettingsFile);
        };

        _btnOpen.Click += async (_, _) => await OpenDialogAsync();

        // Doppelklick auf ein Skript in der Kreuzreferenz führt zu seinem Quelltext.
        _usage.ScriptRequested += id =>
        {
            var tabs = this.FindControl<TabControl>("Tabs")!;
            tabs.SelectedItem = this.FindControl<TabItem>("TabScripts")!;
            if (!_scripts.SelectScript(id))
                _lblStatus.Text = $"Das Skript „{id}\" steht nicht in der Skriptliste.";
        };

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        if (startFile is not null)
            Opened += async (_, _) => await LoadFileAsync(startFile);
    }

    // ------------------------------------------------------------------ Datei öffnen

    private async Task OpenDialogAsync()
    {
        // StorageProvider ist der plattformübergreifende Weg zum Dateidialog: unter
        // Windows der Win32-Dialog, unter macOS NSOpenPanel, unter Linux der Portal-Dialog.
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "ioBroker-Backup öffnen",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("ioBroker-Backup")
                {
                    Patterns = new[] { "*.tar.gz", "*.tgz", "*.json" }
                },
                FilePickerFileTypes.All
            }
        });

        var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (!string.IsNullOrEmpty(path)) await LoadFileAsync(path);
    }

    // DataTransfer/DataFormat ist die aktuelle Schnittstelle seit Avalonia 11.3; die
    // frühere Data/DataFormats-Fassung ist als veraltet markiert.
    private void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var path = e.DataTransfer.TryGetFile()?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path)) await LoadFileAsync(path);
    }

    /// <summary>
    /// Befüllt alle Ansichten mit einem bereits geladenen Backup und setzt Kopfzeile und
    /// Statustext so, wie sie nach einem echten Ladevorgang aussehen. Nur für den
    /// Bildmodus (<c>--screenshots</c>) — sonst stünde auf jedem Bild „Keine Datei geladen".
    /// </summary>
    internal void ApplyDataForScreenshots(BackupData data, string path)
    {
        SetData(data);

        // Nur der Dateiname, nicht der ganze Pfad: Im Betrieb ist der vollständige Pfad
        // hilfreich, auf einem veröffentlichten Bild stünden dort Benutzername und
        // Ordnerstruktur des Rechners, auf dem die Bilder entstanden sind.
        _lblFile.Text = Path.GetFileName(path);
        _lblStatus.Text = data.Kind == BackupKind.Full
            ? $"Geladen: {data.Objects.Count:N0} Objekte, {data.Instances.Count:N0} Instanzen."
            : "Geladen: Skript-Teilbackup — die Übersicht braucht ein Voll-Backup.";
    }

    // ------------------------------------------------------------------ Laden

    private async Task LoadFileAsync(string path)
    {
        // Ein zweiter Ladevorgang bricht den ersten ab, statt sich mit ihm zu überlagern.
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        SetBusy(true);
        _lblFile.Text = path;

        // Fortschrittsmeldungen kommen aus einem Hintergrund-Thread; Progress<T> stellt
        // die Zustellung auf dem UI-Thread sicher, weil es hier erzeugt wird.
        // Siehe die gleichlautende Stelle in der WinForms-Fassung: „Bitte warten" gehört
        // an eine Stelle, nicht in jede einzelne Meldung.
        var progress = new Progress<string>(msg => _lblStatus.Text = Wartetext(msg));

        // Aus dem arbeitenden Thread geschrieben, damit die letzte Zeile auch dann auf der
        // Platte steht, wenn das Fenster nicht mehr reagiert.
        using var log = LoadLog.Start(LoadLog.DefaultPath(), AppInfo.Version, path);

        try
        {
            var data = await BackupLoader.LoadAsync(path, progress, ct, log);
            if (ct.IsCancellationRequested) return;

            // Die schweren Analysen laufen im Hintergrund und nicht mehr dort, wo die
            // Ansichten ihre Daten bekommen — das ist der UI-Thread.
            _lblStatus.Text = Wartetext("Backup wird analysiert …");
            var analysen = await Task.Run(() => AnalysisResults.Compute(data, log, progress, ct), ct);
            if (ct.IsCancellationRequested) return;

            SetData(data, analysen, log);

            // Erst nach erfolgreichem Laden merken — ein Pfad, der nicht funktioniert hat,
            // gehört nicht hinter den Knopf „Zuletzt geöffnet".
            _settings.LastFile = path;
            _settings.Save(SettingsFile);
            _btnRecent.IsEnabled = true;

            _lblStatus.Text = data.Kind == BackupKind.Full
                ? $"Geladen: {data.Objects.Count:N0} Objekte, {data.Instances.Count:N0} Instanzen."
                : "Geladen: Skript-Teilbackup — die Übersicht braucht ein Voll-Backup.";
        }
        catch (OperationCanceledException)
        {
            // Vom Nutzer ausgelöst (neue Datei während des Ladens) — kein Fehlerfall.
        }
        catch (Exception ex)
        {
            SetData(null);
            _lblStatus.Text = "Laden fehlgeschlagen.";
            var wo = $"\n\nDer Ablauf des Ladevorgangs steht in:\n{LoadLog.DefaultPath()}";
            await ShowErrorAsync((ex is NotABackupException
                ? ex.Message
                : $"Die Datei konnte nicht gelesen werden.\n\n{ex.Message}") + wo);
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>
    /// Reicht das geladene Backup an alle portierten Ansichten weiter — oder <c>null</c>,
    /// wenn keines (mehr) vorliegt. Jede Ansicht entscheidet selbst, ob sie mit der
    /// Backup-Art etwas anfangen kann.
    ///
    /// <c>internal</c>, damit der Selbsttest (<c>--selftest</c>) alle Ansichten mit echten
    /// Daten befüllen kann statt nur ihre Konstruktion zu prüfen.
    /// </summary>
    /// <summary>Setzt „Bitte warten" vor eine Fortschrittsmeldung.</summary>
    private static string Wartetext(string schritt) => $"Bitte warten — {schritt}";

    internal void SetData(BackupData? data, AnalysisResults? analysen = null, LoadLog? log = null)
    {
        _data = data;

        void Schritt(string name, Action fuellen)
        {
            log?.Step($"Ansicht: {name}");
            fuellen();
        }

        Schritt("Übersicht", () => _overview.SetData(data));
        Schritt("Backup-Prüfung", () => _backupCheck.SetData(data));
        Schritt("VIS-Datenpunkte", () => _vis.SetData(data, analysen));
        Schritt("Verwaiste Datenpunkte", () => _orphans.SetData(data, analysen));
        Schritt("Datenpunkte", () => _datapoints.SetData(data));
        Schritt("Skripte", () => _scripts.SetData(data));
        Schritt("Verwendung", () => _usage.SetData(data, analysen));
        Schritt("Vergleich", () => _compare.SetData(data));
        Schritt("Logging", () => _logging.SetData(data));
        Schritt("Aliasse", () => _aliases.SetData(data));
        Schritt("Dateien", () => _files.SetData(data));
        log?.Step("Alle Ansichten aufgebaut");
    }

    private void SetBusy(bool busy)
    {
        _progress.IsVisible = busy;
        _btnOpen.IsEnabled = !busy;
        if (busy) _lblStatus.Text = "Backup wird gelesen …";
    }

    /// <summary>
    /// Schlichter Meldungsdialog. Avalonia bringt keinen MessageBox-Ersatz mit, und für
    /// eine einzelne Fehlermeldung lohnt keine zusätzliche Abhängigkeit.
    /// </summary>
    private async Task ShowErrorAsync(string message)
    {
        var ok = new Button { Content = "OK", Width = 90, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right };

        var dlg = new Window
        {
            Title = "Fehler",
            Width = 520,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new global::Avalonia.Thickness(16),
                Spacing = 14,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap },
                    ok
                }
            }
        };

        ok.Click += (_, _) => dlg.Close();
        await dlg.ShowDialog(this);
    }

    /// <summary>
    /// Setzt die Darstellung. <see cref="ThemeVariant.Default"/> heißt: der Einstellung des
    /// Betriebssystems folgen — genau deshalb sieht die App unter einem dunkel gestellten
    /// Windows dunkel aus, während die WinForms-Fassung daneben hell bleibt (sie kennt
    /// den Dark Mode nicht).
    /// </summary>
    private static void ApplyTheme(ThemeChoice choice)
    {
        if (Application.Current is null) return;

        Application.Current.RequestedThemeVariant = choice switch
        {
            ThemeChoice.Light => ThemeVariant.Light,
            ThemeChoice.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();

        // Fenstergröße nur im normalen Zustand merken — sonst stünde beim nächsten Start
        // die Bildschirmgröße als „Wunschgröße" in der Datei.
        _settings.Maximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            _settings.WindowWidth = (int)Width;
            _settings.WindowHeight = (int)Height;
        }
        _settings.Save(SettingsFile);

        base.OnClosed(e);
    }
}

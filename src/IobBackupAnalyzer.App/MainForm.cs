using IobBackupAnalyzer.Core;

namespace IobBackupAnalyzer.App;

public sealed class MainForm : Form
{
    /// <summary>Bisheriger Dateiname — bestehende Einstellungen bleiben damit gültig.</summary>
    private const string SettingsFile = "einstellungen.json";

    private readonly UserSettings _settings;

    private readonly Button _btnOpen = new();
    private readonly Label _lblDrop = new();
    private readonly Label _lblFile = new();
    private readonly Button _btnRecent = new();
    private readonly ProgressBar _progress = new();

    private readonly TabControl _tabs = new();
    private readonly TabPage _pageOverview = new("Übersicht");
    private readonly TabPage _pageBackupCheck = new("Backup-Prüfung");
    private readonly TabPage _pageScripts = new("Skripte");
    private readonly TabPage _pageUsage = new("Verwendung");
    private readonly TabPage _pageVis = new("VIS-Datenpunkte");
    private readonly TabPage _pageOrphans = new("Verwaiste Datenpunkte");
    private readonly TabPage _pageLogging = new("Logging");
    private readonly TabPage _pageAliases = new("Aliasse");
    private readonly TabPage _pageFiles = new("Dateien");
    private readonly TabPage _pageCompare = new("Vergleich");
    private readonly TabPage _pageHelp = new("Hilfe");
    private readonly TabPage _pageChangelog = new("Änderungen");

    private readonly OverviewTab _overview = new();
    private readonly BackupCheckTab _backupCheck = new();
    private readonly ScriptsTab _scripts = new();
    private readonly UsageTab _usage = new();
    private readonly VisTab _vis = new();
    private readonly OrphansTab _orphans = new();
    private readonly LoggingTab _logging = new();
    private readonly AliasTab _aliases = new();
    private readonly FilesTab _files = new();
    private readonly CompareTab _compare = new();
    private readonly HelpTab _help = new();

    // Derselbe Renderer, andere Absätze — als Abschnitt der Hilfe war der Verlauf zu lang geworden.
    private readonly HelpTab _changelog = new(ChangelogContent.Blocks);

    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _statusText = new();
    private readonly ToolStripStatusLabel _versionLabel = new();

    private BackupData? _data;
    private CancellationTokenSource? _cts;

    public MainForm(string? startFile)
    {
        _settings = UserSettings.Load(SettingsFile);
        BuildUi();

        Shown += async (_, _) =>
        {
            if (startFile is not null) await LoadFileAsync(startFile);
        };
    }

    /// <summary>
    /// Setzt das Titelleisten-Icon aus dem in die EXE eingebetteten Anwendungs-Icon.
    /// Schlägt das fehl (etwa beim Start ohne Icon-Ressource), bleibt das Standard-Icon.
    /// </summary>
    private void TrySetWindowIcon()
    {
        try
        {
            var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (icon is not null) Icon = icon;
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.FileNotFoundException)
        {
            // ohne Icon weiterlaufen
        }
    }
    /// <summary>
    /// Alle Ergebnislisten des Fensters, quer durch die Tabs und deren Unterbereiche.
    /// </summary>
    private static IEnumerable<ListView> AllListViews(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child is ListView list) yield return list;

            foreach (var nested in AllListViews(child))
                yield return nested;
        }
    }

    private void BuildUi()
    {
        // Version in der Titelleiste — auf einen Blick sichtbar, welcher Stand läuft.
        Text = AppInfo.WindowTitle;
        TrySetWindowIcon();
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 600);
        ClientSize = new Size(_settings.WindowWidth, _settings.WindowHeight);
        if (_settings.Maximized) WindowState = FormWindowState.Maximized;
        AllowDrop = true;
        Font = new Font("Segoe UI", 9F);

        // ---------- Kopfbereich: Dateiauswahl ----------
        var header = TabLayout.TopBar(92, new Padding(10, 10, 10, 6));

        _btnOpen.Text = "Backup öffnen …";
        _btnOpen.Size = new Size(150, 34);
        _btnOpen.Location = new Point(10, 10);
        _btnOpen.Click += async (_, _) => await OpenDialogAsync();

        _lblDrop.Text = "… oder Datei hier hineinziehen";
        _lblDrop.TextAlign = ContentAlignment.MiddleCenter;
        _lblDrop.BorderStyle = BorderStyle.FixedSingle;
        _lblDrop.ForeColor = SystemColors.GrayText;
        _lblDrop.Location = new Point(170, 10);
        _lblDrop.Size = new Size(300, 34);
        _lblDrop.AllowDrop = true;

        _btnRecent.Text = "Zuletzt geöffnet";
        _btnRecent.Size = new Size(140, 34);
        _btnRecent.Location = new Point(480, 10);
        _btnRecent.Enabled = _settings.LastFile is not null && File.Exists(_settings.LastFile);
        if (_btnRecent.Enabled) _btnRecent.Tag = _settings.LastFile;
        _btnRecent.Click += async (_, _) =>
        {
            if (_btnRecent.Tag is string p) await LoadFileAsync(p);
        };

        _lblFile.Text = "Keine Datei geladen.";
        _lblFile.Location = new Point(12, 52);
        _lblFile.Size = new Size(900, 20);
        _lblFile.ForeColor = SystemColors.GrayText;
        _lblFile.AutoEllipsis = true;
        _lblFile.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        _progress.Location = new Point(630, 10);
        _progress.Size = new Size(200, 34);
        _progress.Style = ProgressBarStyle.Marquee;
        _progress.Visible = false;

        header.Controls.AddRange(new Control[] { _btnOpen, _lblDrop, _btnRecent, _progress, _lblFile });

        // ---------- Tabs ----------
        _tabs.Dock = DockStyle.Fill;
        _tabs.Padding = new Point(12, 5);

        _overview.Dock = DockStyle.Fill;
        _backupCheck.Dock = DockStyle.Fill;
        _scripts.Dock = DockStyle.Fill;
        _usage.Dock = DockStyle.Fill;
        _vis.Dock = DockStyle.Fill;
        _orphans.Dock = DockStyle.Fill;
        _logging.Dock = DockStyle.Fill;
        _aliases.Dock = DockStyle.Fill;
        _files.Dock = DockStyle.Fill;
        _compare.Dock = DockStyle.Fill;
        _help.Dock = DockStyle.Fill;
        _changelog.Dock = DockStyle.Fill;

        _pageOverview.Controls.Add(_overview);
        _pageBackupCheck.Controls.Add(_backupCheck);
        _pageScripts.Controls.Add(_scripts);
        _pageUsage.Controls.Add(_usage);
        _pageVis.Controls.Add(_vis);
        _pageOrphans.Controls.Add(_orphans);
        _pageLogging.Controls.Add(_logging);
        _pageAliases.Controls.Add(_aliases);
        _pageFiles.Controls.Add(_files);
        _pageCompare.Controls.Add(_compare);
        _pageHelp.Controls.Add(_help);
        _pageChangelog.Controls.Add(_changelog);
        _tabs.TabPages.AddRange(new[]
        {
            _pageOverview, _pageBackupCheck, _pageScripts, _pageUsage, _pageVis, _pageOrphans,
            _pageLogging, _pageAliases, _pageFiles, _pageCompare, _pageHelp, _pageChangelog
        });

        // Doppelklick auf ein Skript in der Kreuzreferenz führt zu seinem Quelltext.
        _usage.ScriptRequested += id =>
        {
            _tabs.SelectedTab = _pageScripts;
            if (!_scripts.SelectScript(id))
                _statusText.Text = $"Das Skript „{id}\" steht nicht in der Skriptliste.";
        };

        // ---------- Statusleiste ----------
        _statusText.Text = "Bereit.";
        _statusText.Spring = true;
        _statusText.TextAlign = ContentAlignment.MiddleLeft;

        // Version rechts in der Statusleiste, dauerhaft sichtbar.
        _versionLabel.Text = AppInfo.LongVersion;
        _versionLabel.ForeColor = SystemColors.GrayText;
        _versionLabel.TextAlign = ContentAlignment.MiddleRight;
        _versionLabel.ToolTipText = "Laufender Programmstand";

        _status.Items.Add(_statusText);
        _status.Items.Add(_versionLabel);

        Controls.Add(_tabs);
        Controls.Add(header);
        Controls.Add(_status);

        // Rechtsklick auf einen Spaltenkopf passt die Spalte an ihren Inhalt an — an einer
        // Stelle für alle Listen, damit keine vergessen wird, wenn eine dazukommt.
        foreach (var list in AllListViews(this))
            ListViewAutoFit.Attach(list);

        // Drag & Drop auf Fenster und Ablagefläche.
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
        _lblDrop.DragEnter += OnDragEnter;
        _lblDrop.DragDrop += OnDragDrop;

        SetAvailability(null);

        FormClosing += (_, _) =>
        {
            _cts?.Cancel();
            _settings.Maximized = WindowState == FormWindowState.Maximized;
            if (WindowState == FormWindowState.Normal)
            {
                _settings.WindowWidth = ClientSize.Width;
                _settings.WindowHeight = ClientSize.Height;
            }
            _settings.Save(SettingsFile);
        };
    }

    // ---------------------------------------------------------------- Laden

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
        {
            e.Effect = DragDropEffects.Copy;
            _lblDrop.BackColor = SystemColors.Highlight;
            _lblDrop.ForeColor = SystemColors.HighlightText;
        }
    }

    private async void OnDragDrop(object? sender, DragEventArgs e)
    {
        _lblDrop.BackColor = SystemColors.Control;
        _lblDrop.ForeColor = SystemColors.GrayText;

        if (e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            await LoadFileAsync(files[0]);
    }

    private async Task OpenDialogAsync()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "ioBroker-Backup auswählen",
            Filter = "ioBroker-Backups (*.tar.gz;*.json;*.jsonl)|*.tar.gz;*.json;*.jsonl|Alle Dateien (*.*)|*.*",
            CheckFileExists = true
        };

        if (_settings.LastFile is not null)
        {
            var dir = Path.GetDirectoryName(_settings.LastFile);
            if (dir is not null && Directory.Exists(dir)) dlg.InitialDirectory = dir;
        }

        if (dlg.ShowDialog(this) == DialogResult.OK)
            await LoadFileAsync(dlg.FileName);
    }

    private async Task LoadFileAsync(string path)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        SetBusy(true, "Datei wird gelesen …");
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Das Protokoll wird vom jeweils arbeitenden Thread geschrieben, nicht über die
        // Fortschrittsmeldung: Sonst fehlte genau die letzte Zeile, wenn es der UI-Thread
        // ist, der nicht mehr weiterkommt.
        using var log = LoadLog.Start(LoadLog.DefaultPath(), AppInfo.Version, path);

        try
        {
            // „Bitte warten" steht an einer einzigen Stelle und gilt damit für jede
            // Fortschrittsmeldung — beim Lesen, beim Auswerten und beim Aufbauen der Tabs.
            // Ein Schritt, der nur seinen Namen nennt, sieht aus wie ein Ergebnis; hier
            // soll er aussehen wie das, was er ist: etwas, das gerade läuft.
            var progress = new Progress<string>(msg => _statusText.Text = Wartetext(msg));
            var data = await BackupLoader.LoadAsync(path, progress, _cts.Token, log);

            // Die schweren Analysen laufen im Hintergrund, nicht beim Füllen der Tabs.
            // Vorher blockierten sie das Fenster für ihre gesamte Rechenzeit.
            _statusText.Text = Wartetext("Backup wird analysiert …");
            var analysen = await Task.Run(
                () => AnalysisResults.Compute(data, log, progress, _cts.Token), _cts.Token);

            sw.Stop();

            _data = data;
            _settings.LastFile = path;
            _settings.Save(SettingsFile);
            _btnRecent.Tag = path;
            _btnRecent.Enabled = true;

            ApplyData(data, analysen, log);

            var kind = data.Kind == BackupKind.Full ? "Voll-Backup" : "Skript-Backup";
            _lblFile.Text = $"{Path.GetFileName(path)}   ·   {kind}   ·   " +
                            $"Backup vom {data.CreatedAt:dd.MM.yyyy HH:mm}   ·   {path}";
            _lblFile.ForeColor = SystemColors.ControlText;

            var msg = $"Geladen in {sw.ElapsedMilliseconds} ms   ·   {data.Objects.Count:N0} Objekte   ·   " +
                      $"{data.Scripts.Count:N0} Skripte";
            if (data.Kind == BackupKind.Full) msg += $"   ·   {data.StateCount:N0} States";
            if (data.SkippedCount > 0) msg += $"   ·   {data.SkippedCount} Objekte übersprungen";
            _statusText.Text = msg;

            if (data.SkippedCount > 0)
            {
                MessageBox.Show(this,
                    $"{data.SkippedCount} Objekte konnten nicht gelesen werden und wurden übersprungen.\r\n\r\n" +
                    "Alle übrigen Daten wurden vollständig geladen.",
                    "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (OperationCanceledException)
        {
            _statusText.Text = "Ladevorgang abgebrochen.";
        }
        catch (NotABackupException ex)
        {
            ShowLoadError(path, ex.Message, ex, log: false);
        }
        catch (InvalidDataException ex)
        {
            ShowLoadError(path,
                "Das Archiv konnte nicht entpackt werden. Die Datei ist beschädigt oder " +
                "kein gültiges tar.gz-Archiv.", ex, log: true);
        }
        catch (Exception ex)
        {
            ShowLoadError(path, "Die Datei konnte nicht gelesen werden:\r\n\r\n" + ex.Message, ex, log: true);
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    /// <summary>
    /// Setzt „Bitte warten" vor eine Fortschrittsmeldung. Eine eigene Methode, damit die
    /// Formulierung an einer Stelle steht und nicht in zehn Zeichenketten gepflegt wird.
    /// </summary>
    private static string Wartetext(string schritt) => $"Bitte warten — {schritt}";

    private void ShowLoadError(string path, string message, Exception ex, bool log)
    {
        if (log) Program.LogError($"Laden fehlgeschlagen: {path}", ex);

        _statusText.Text = "Laden fehlgeschlagen.";
        var extra = log ? "\r\n\r\nDetails wurden protokolliert in:\r\n" + Program.ErrorLogPath : "";

        // Der Ladevorgang selbst wird ebenfalls mitgeschrieben. Der Pfad gehört hierher,
        // weil man ihn genau dann sucht — und weil das Protokoll auch dann etwas hergibt,
        // wenn gar kein Fehler kam, sondern das Fenster einfach stehen blieb.
        extra += "\r\n\r\nDer Ablauf des Ladevorgangs steht in:\r\n" + LoadLog.DefaultPath();

        MessageBox.Show(this,
            $"Datei: {Path.GetFileName(path)}\r\n\r\n{message}{extra}",
            "Backup konnte nicht geladen werden", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    /// <summary>
    /// Zugang für <see cref="Screenshots"/>: schaltet die Tabs für die Aufnahmen durch.
    /// <c>internal</c> aus demselben Grund wie <see cref="ApplyDataForScreenshots"/> —
    /// die Bilder sollen aus der echten Oberfläche entstehen, nicht aus einem Nachbau.
    /// </summary>
    internal TabControl TabsForScreenshots => _tabs;

    /// <summary>
    /// Befüllt alle Tabs mit einem bereits geladenen Backup, ohne den Dateidialog und ohne
    /// den asynchronen Ladeweg. Nur für den Bildmodus (<c>--screenshots</c>).
    ///
    /// Kopfzeile und Statustext werden mitgesetzt: Auf einem Bild soll dasselbe stehen wie
    /// nach einem echten Ladevorgang — sonst zeigt jedes Bild „Keine Datei geladen".
    /// </summary>
    internal void ApplyDataForScreenshots(BackupData data, string path)
    {
        _data = data;

        ApplyData(data);

        var kind = data.Kind == BackupKind.Full ? "Voll-Backup" : "Skript-Backup";
        _lblFile.Text = $"{Path.GetFileName(path)}   ·   {kind}   ·   " +
                        $"Backup vom {data.CreatedAt:dd.MM.yyyy HH:mm}";
        _lblFile.ForeColor = SystemColors.ControlText;

        var msg = $"{data.Objects.Count:N0} Objekte   ·   {data.Scripts.Count:N0} Skripte";
        if (data.Kind == BackupKind.Full) msg += $"   ·   {data.StateCount:N0} States";
        _statusText.Text = msg;
    }

    private void ApplyData(BackupData data, AnalysisResults? analysen = null, LoadLog? log = null)
    {
        // Die Tabs bekommen die fertigen Analysen und füllen nur noch ihre Listen. Das
        // dauert trotzdem einen Moment — deshalb sagt die Statuszeile, welcher Tab gerade
        // dran ist, und das Fenster darf sich zwischendurch neu zeichnen. Ohne das steht
        // die Beschriftung des letzten Ladeschritts da, während scheinbar nichts passiert.
        void Schritt(string name, Action fuellen)
        {
            _statusText.Text = Wartetext($"{name} wird aufgebaut …");
            log?.Step($"Tab: {name}");
            Application.DoEvents();
            fuellen();
        }

        Schritt("Skripte", () => _scripts.SetData(data));
        Schritt("Verwendung", () => _usage.SetData(data, analysen));
        Schritt("Übersicht", () => _overview.SetData(data));
        Schritt("Backup-Prüfung", () => _backupCheck.SetData(data));
        Schritt("VIS-Datenpunkte", () => _vis.SetData(data, analysen));
        Schritt("Verwaiste Datenpunkte", () => _orphans.SetData(data, analysen));
        Schritt("Logging", () => _logging.SetData(data));
        Schritt("Aliasse", () => _aliases.SetData(data));
        Schritt("Dateien", () => _files.SetData(data));
        Schritt("Vergleich", () => _compare.SetData(data));
        SetAvailability(data);
        log?.Step("Alle Tabs aufgebaut");

        // Bei einem reinen Skript-Backup direkt auf den einzigen nutzbaren Tab springen.
        _tabs.SelectedTab = data.Kind == BackupKind.Full ? _pageOverview : _pageScripts;
    }

    /// <summary>
    /// Säulen 1 und 3 brauchen ein Voll-Backup. Statt die Tabs auszugrauen (WinForms
    /// blendet deaktivierte TabPages nicht sichtbar aus), zeigen sie einen Hinweis.
    /// </summary>
    private void SetAvailability(BackupData? data)
    {
        var full = data?.Kind == BackupKind.Full;
        _overview.SetAvailable(full);
        _backupCheck.SetAvailable(full);
        _vis.SetAvailable(full);
        _orphans.SetAvailable(full);
        _logging.SetAvailable(full);
        _aliases.SetAvailable(full);
        _scripts.SetAvailable(data is not null);

        // Die Kreuzreferenz braucht den Objektbestand: Ohne ihn ist nicht entscheidbar, ob
        // eine Zeichenkette im Skript ein Datenpunkt ist oder bloß irgendein Text.
        _usage.SetAvailable(full);

        // Der Dateibereich hängt nicht am Backup-Typ, sondern daran, ob das Archiv
        // überhaupt einen files/-Baum mitbringt.
        _files.SetAvailable(data is not null && data.Files.Count > 0);

        // Der Vergleich funktioniert auch mit zwei Skript-Backups — er braucht nur
        // überhaupt eine geladene Datei.
        _compare.SetAvailable(data is not null);
    }

    private void SetBusy(bool busy, string? message)
    {
        _progress.Visible = busy;
        _btnOpen.Enabled = !busy;
        _btnRecent.Enabled = !busy && _btnRecent.Tag is string;
        _tabs.Enabled = !busy;
        if (message is not null) _statusText.Text = message;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        Application.DoEvents();
    }
}

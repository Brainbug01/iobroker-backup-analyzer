using IobBackupAnalyzer.Core;

namespace IobBackupAnalyzer.Web.Services;

/// <summary>
/// Der Bedienzustand aller Reiter — Filtertexte, Auswahlfelder, angeklickte Zeilen.
///
/// <b>Warum das hier steht und nicht in den Reitern selbst:</b> In den Desktop-Fassungen
/// existiert jede Ansicht dauerhaft, auch wenn man gerade eine andere ansieht — ein
/// gesetzter Filter steht beim Zurückwechseln noch da. Im Browser wird immer nur der
/// sichtbare Reiter erzeugt; alles, was in seinen Feldern stünde, wäre beim Wechsel weg.
/// Der Zustand liegt deshalb außerhalb und überlebt den Wechsel.
///
/// Hier steht ausschließlich der Bedienzustand. Die ausgewerteten Daten selbst kommen aus
/// <see cref="AppState"/> und werden nicht doppelt gehalten.
/// </summary>
public sealed class UiState
{
    public OverviewTab Overview { get; private set; } = new();
    public BackupCheckTab BackupCheck { get; private set; } = new();
    public ScriptsTab Scripts { get; private set; } = new();
    public UsageTab Usage { get; private set; } = new();
    public VisTab Vis { get; private set; } = new();
    public OrphansTab Orphans { get; private set; } = new();
    public LoggingTab Logging { get; private set; } = new();
    public AliasTab Alias { get; private set; } = new();
    public DatapointsTab Datapoints { get; private set; } = new();
    public FilesTab Files { get; private set; } = new();
    public CompareTab Compare { get; private set; } = new();

    /// <summary>
    /// Setzt alles zurück. Muss bei jedem neuen Backup laufen: Eine gemerkte Auswahl zeigt
    /// sonst auf eine Zeile, die es nicht mehr gibt, und ein Filter auf einen Adapter, den
    /// das neue Backup gar nicht kennt.
    /// </summary>
    public void Reset()
    {
        Overview = new OverviewTab();
        BackupCheck = new BackupCheckTab();
        Scripts = new ScriptsTab();
        Usage = new UsageTab();
        Vis = new VisTab();
        Orphans = new OrphansTab();
        Logging = new LoggingTab();
        Alias = new AliasTab();
        Datapoints = new DatapointsTab();
        DatapointCache = null;
        WidgetSetCache = null;
        Files = new FilesTab();
        Compare = new CompareTab();
    }

    public sealed class OverviewTab
    {
        public string Filter { get; set; } = "";
    }

    public sealed class BackupCheckTab
    {
        public bool OnlyProblems { get; set; }
    }

    public sealed class ScriptsTab
    {
        public string Search { get; set; } = "";
        public ScriptSearchMode Mode { get; set; } = ScriptSearchMode.NameAndPath;
        public int TypeIndex { get; set; }
        public bool HideDisabled { get; set; }
        public bool OnlyWithHints { get; set; }
        public bool WithGeneratedJs { get; set; }
        public bool ShowXml { get; set; }
        public ScriptInfo? Selected { get; set; }
        public HashSet<ScriptInfo> Marked { get; set; } = new();

        /// <summary>
        /// Ein Skript, zu dem der Reiter beim nächsten Anzeigen springen soll — gesetzt vom
        /// Doppelklick in der Kreuzreferenz. Der Reiter räumt den Wunsch nach dem Sprung
        /// wieder weg, sonst spränge er bei jedem Zurückwechseln erneut.
        /// </summary>
        public string? PendingJump { get; set; }
    }

    public sealed class UsageTab
    {
        public UsageDirection Direction { get; set; } = UsageDirection.ByScript;
        public bool OnlyWithStates { get; set; } = true;
        public UsageStateFilter StateFilter { get; set; } = UsageStateFilter.Alle;
        public string Filter { get; set; } = "";
        public ScriptUsage? SelectedScript { get; set; }
        public StateUsage? SelectedState { get; set; }
    }

    /// <summary>Aufbereitete Widget-Satz-Liste. Siehe <see cref="DatapointCache"/>.</summary>
    public List<WidgetSetRow>? WidgetSetCache { get; set; }

    public sealed class VisTab
    {
        /// <summary>0 = Datenpunkte, 1 = Widget-Saetze.</summary>
        public int Sub { get; set; }

        /// <summary>Der gewählte Widget-Satz — seine Fundstellen stehen darunter.</summary>
        public WidgetSetRow? SelectedSet { get; set; }

        public string Filter { get; set; } = "";
        public VisScope Scope { get; set; } = VisScope.All;
        public VisDatapoint? Selected { get; set; }
        public int ProjectIndex { get; set; }
        public bool WithAssets { get; set; } = true;
    }

    public sealed class OrphansTab
    {
        /// <summary>0 = A, 1 = B, 2 = C — wie die Untertabs der Desktop-Fassungen.</summary>
        public int Sub { get; set; }

        public string FilterA { get; set; } = "";
        public string FilterB { get; set; } = "";
        public bool ShowAllB { get; set; }
        public string FilterC { get; set; } = "";
        public StateView ViewC { get; set; } = StateView.WithoutObject;
    }

    public sealed class LoggingTab
    {
        public string Filter { get; set; } = "";
        public LoggingScope Scope { get; set; } = LoggingScope.All;
    }

    public sealed class AliasTab
    {
        public string Filter { get; set; } = "";
        public AliasScope Scope { get; set; } = AliasScope.All;
        public AliasRow? Selected { get; set; }
        public ConverterGenerator.Result? Generated { get; set; }
    }

    /// <summary>
    /// Die aufbereitete Datenpunktliste. Sie steht hier und nicht im Reiter, weil ihr Aufbau
    /// über alle state-Objekte und alle Werte läuft — bei jedem Tastendruck im Suchfeld neu
    /// gebaut, wäre die Suche in einer großen Anlage unbenutzbar. <see cref="Reset"/> leert
    /// sie beim Laden eines anderen Backups.
    /// </summary>
    public List<DatapointHit>? DatapointCache { get; set; }

    public sealed class DatapointsTab
    {
        public string Filter { get; set; } = "";

        /// <summary>Der gewählte Datenpunkt — sein Wert steht unter der Liste.</summary>
        public DatapointHit? Selected { get; set; }
    }

    public sealed class FilesTab
    {
        public string Filter { get; set; } = "";
        public string Namespace { get; set; } = FilesPresenter.AllNamespaces;
        public HashSet<BackupFileInfo> Marked { get; set; } = new();
        public BackupFileInfo? Selected { get; set; }
    }

    public sealed class CompareTab
    {
        /// <summary>Das zweite Backup — es gehört zum Reiter und nicht zum Programmzustand.</summary>
        public BackupData? Other { get; set; }

        public BackupComparison? Comparison { get; set; }
        public string OtherName { get; set; } = "";

        /// <summary>0 = Kennzahlen … 4 = VIS-Views.</summary>
        public int Sub { get; set; }

        public bool OnlyChangedInstances { get; set; } = true;
        public bool OnlyChangedScripts { get; set; } = true;
        public bool OnlyChangedLines { get; set; } = true;
        public bool OnlyChangedViews { get; set; } = true;
        public ScriptChange? SelectedScript { get; set; }
        public NamespaceChange? SelectedNamespace { get; set; }
    }
}

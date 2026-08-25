using IobBackupAnalyzer.Core;

namespace IobBackupAnalyzer.Web.Services;

/// <summary>
/// Das geladene Backup und seine vorberechneten Analysen — die eine Stelle, an der alle
/// Reiter ihre Daten holen.
///
/// Entspricht dem, was in den Desktop-Fassungen das Hauptfenster hält und per
/// <c>SetData</c> an die Ansichten weiterreicht. Hier melden sich die Reiter stattdessen
/// über <see cref="Changed"/> an: Ein Browser-Reiter, der gerade nicht sichtbar ist,
/// existiert nicht als Steuerelement, an das man etwas übergeben könnte.
/// </summary>
public sealed class AppState
{
    private BackupData? _data;

    public BackupData? Data
    {
        get => _data;
        private set => _data = value;
    }

    /// <summary>
    /// Im Hintergrund vorberechnete Analysen. In der Browser-Fassung ist „Hintergrund"
    /// wörtlich zu nehmen: Gerechnet wird auf demselben Faden wie die Anzeige (siehe
    /// <c>MainLayout</c>), vorberechnet wird trotzdem — sonst rechnete jeder Reiter beim
    /// ersten Anzeigen erneut.
    /// </summary>
    public AnalysisResults? Analysis { get; private set; }

    /// <summary>Name der geladenen Datei, wie er in der Kopfzeile steht.</summary>
    public string FileName { get; private set; } = "";

    /// <summary>Größe der geladenen Datei in Bytes — für die Kopfzeile.</summary>
    public long FileSize { get; private set; }

    /// <summary>Die Zeilen des Ladeprotokolls, in der Reihenfolge ihres Entstehens.</summary>
    public IReadOnlyList<string> LoadLogLines => _log;
    private readonly List<string> _log = new();

    /// <summary>Meldet jede Änderung an die Reiter.</summary>
    public event Action? Changed;

    public void Set(BackupData? data, AnalysisResults? analysis, string fileName, long fileSize,
                    IEnumerable<string>? logLines = null)
    {
        Data = data;
        Analysis = analysis;
        FileName = fileName;
        FileSize = fileSize;

        _log.Clear();
        if (logLines is not null) _log.AddRange(logLines);

        Changed?.Invoke();
    }

    /// <summary>Meldet eine Änderung, ohne die Daten auszutauschen — etwa nach einem Filter.</summary>
    public void NotifyChanged() => Changed?.Invoke();
}

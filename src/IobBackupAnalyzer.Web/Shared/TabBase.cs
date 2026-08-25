using IobBackupAnalyzer.Core;
using IobBackupAnalyzer.Web.Services;
using Microsoft.AspNetCore.Components;

namespace IobBackupAnalyzer.Web.Shared;

/// <summary>
/// Gemeinsamer Unterbau aller Reiter: Zugriff auf das geladene Backup, auf den
/// Bedienzustand und auf die Browser-Dienste — und das Nachzeichnen, wenn ein neues
/// Backup geladen wurde.
///
/// Entspricht dem, was in den Desktop-Fassungen der Aufruf <c>SetData</c> erledigt. Dort
/// reicht das Hauptfenster die Daten an jede Ansicht durch; hier holt sich jeder Reiter
/// sie selbst, weil immer nur der sichtbare existiert.
/// </summary>
public abstract class TabBase : ComponentBase, IDisposable
{
    [Inject] protected AppState Zustand { get; set; } = default!;
    [Inject] protected UiState Bedienung { get; set; } = default!;
    [Inject] protected BrowserIo Io { get; set; } = default!;
    [Inject] protected DialogService Dialoge { get; set; } = default!;

    /// <summary>Das geladene Backup, oder null.</summary>
    protected BackupData? Daten => Zustand.Data;

    /// <summary>Die beim Laden vorberechneten Analysen, oder null.</summary>
    protected AnalysisResults? Analysen => Zustand.Analysis;

    /// <summary>
    /// Der Platzhaltertext, wenn kein Backup geladen ist oder es kein Voll-Backup ist.
    /// Wortlaut wie in den Desktop-Fassungen; <paramref name="statt"/> ist der Satz, der
    /// erklärt, warum dieser Reiter ein Voll-Backup braucht.
    /// </summary>
    protected string PlatzhalterText(string statt) =>
        Daten is null
            ? "Kein Backup geladen.\n\nBitte oben eine Datei öffnen oder hineinziehen."
            : statt;

    protected override void OnInitialized() => Zustand.Changed += Neuzeichnen;

    private void Neuzeichnen() => InvokeAsync(StateHasChanged);

    public virtual void Dispose() => Zustand.Changed -= Neuzeichnen;
}

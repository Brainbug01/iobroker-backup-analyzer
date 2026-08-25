namespace IobBackupAnalyzer.Web.Services;

/// <summary>
/// Meldungs- und Rückfragefenster, gemeinsam für alle Reiter — das Gegenstück zu
/// <c>Dialogs</c> in der Avalonia-Fassung.
///
/// Bewusst nicht <c>window.alert</c> und <c>window.confirm</c>: Die sperren den Browser
/// hart, sehen in jedem Browser anders aus und lassen sich nicht mit den langen,
/// mehrzeiligen Texten füllen, die dieses Programm zeigt.
/// </summary>
public sealed class DialogService
{
    /// <summary>Der gerade offene Dialog, oder null.</summary>
    public DialogRequest? Current { get; private set; }

    public event Action? Changed;

    /// <summary>Ein Hinweis mit einem OK-Knopf.</summary>
    public Task MessageAsync(string title, string message) =>
        ShowAsync(new DialogRequest(title, message, Confirm: false));

    /// <summary>
    /// Ja/Nein-Rückfrage. „Nein" ist vorbelegt: Diese Frage kommt nur in Fällen, in denen
    /// Weitermachen Erklärungsbedarf hat — dann soll ein versehentliches Enter abbrechen.
    /// Gleiche Begründung wie in der Avalonia-Fassung.
    /// </summary>
    public Task<bool> ConfirmAsync(string title, string message) =>
        ShowAsync(new DialogRequest(title, message, Confirm: true));

    private Task<bool> ShowAsync(DialogRequest request)
    {
        Current = request;
        Changed?.Invoke();
        return request.Completion.Task;
    }

    /// <summary>Schließt den Dialog mit dem gewählten Ergebnis.</summary>
    public void Close(bool result)
    {
        var open = Current;
        Current = null;
        Changed?.Invoke();
        open?.Completion.TrySetResult(result);
    }

    public sealed record DialogRequest(string Title, string Message, bool Confirm)
    {
        internal TaskCompletionSource<bool> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

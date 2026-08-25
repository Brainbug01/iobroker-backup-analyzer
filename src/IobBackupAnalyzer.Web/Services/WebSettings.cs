using IobBackupAnalyzer.Core;
using Microsoft.JSInterop;

namespace IobBackupAnalyzer.Web.Services;

/// <summary>
/// Was sich die Browser-Fassung merkt — und was bewusst nicht.
///
/// Von den Einstellungen der Desktop-Fassungen (<see cref="UserSettings"/>) bleibt hier
/// allein die Darstellung übrig: Fenstergröße gibt es im Browser nicht, und „zuletzt
/// geöffnet" wäre eine leere Zusage — die Seite darf eine Datei nicht von sich aus
/// wieder öffnen, und der Pfad des Anwenders ist ihr ohnehin nicht bekannt.
///
/// Abgelegt wird im Speicher des Browsers, getrennt je Adresse. Es verlässt den Rechner
/// nichts.
/// </summary>
public sealed class WebSettings
{
    private const string ThemeKey = "iob-analyzer-darstellung";

    private readonly IJSRuntime _js;

    public WebSettings(IJSRuntime js) => _js = js;

    public ThemeChoice Theme { get; private set; } = ThemeChoice.System;

    /// <summary>Beschriftungen der Darstellungswahl, in der Reihenfolge von <see cref="ThemeChoice"/>.</summary>
    public static readonly string[] ThemeLabels = { "System", "Hell", "Dunkel" };

    /// <summary>Liest die gemerkte Darstellung und wendet sie an. Einmal beim Start.</summary>
    public async Task InitAsync()
    {
        var stored = await _js.InvokeAsync<string?>("iobAnalyzer.lesen", ThemeKey);

        Theme = stored switch
        {
            "hell" => ThemeChoice.Light,
            "dunkel" => ThemeChoice.Dark,
            _ => ThemeChoice.System
        };

        await ApplyAsync();
    }

    public async Task SetThemeAsync(ThemeChoice choice)
    {
        Theme = choice;
        await _js.InvokeVoidAsync("iobAnalyzer.schreiben", ThemeKey, Name(choice));
        await ApplyAsync();
    }

    private async Task ApplyAsync() =>
        await _js.InvokeVoidAsync("iobAnalyzer.themaSetzen", Name(Theme));

    private static string Name(ThemeChoice choice) => choice switch
    {
        ThemeChoice.Light => "hell",
        ThemeChoice.Dark => "dunkel",
        _ => "system"
    };
}

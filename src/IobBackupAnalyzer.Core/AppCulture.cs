using System.Globalization;

namespace IobBackupAnalyzer.Core;

/// <summary>
/// Legt die Anzeigekultur fest — ein Pflegeort für beide Oberflächen.
///
/// Die Oberfläche ist durchgehend deutsch beschriftet, die Zahlen richteten sich aber nach
/// der Kultur des Betriebssystems. Unter Windows (de-DE) stand da „16.576", auf einem Linux
/// ohne deutsche Spracheinstellung „16,576" — dieselbe App, zwei Zahlbilder. Betroffen war
/// jede Stelle mit „:N0", und davon gibt es rund 76 quer durch Core, WinForms und Avalonia.
///
/// Aufgefallen ist es erst beim ersten Test der plattformübergreifenden Fassung auf einem
/// echten Linux: Ein frisch aufgesetztes System hat keine deutsche Spracheinstellung, und
/// genau so sieht ein typischer ioBroker-Rechner aus.
///
/// Die Kulturdaten stammen aus ICU. Ob auf dem System eine deutsche Spracheinstellung
/// erzeugt wurde, spielt dafür keine Rolle — ICU bringt sie mit.
/// </summary>
public static class AppCulture
{
    /// <summary>
    /// Setzt Deutsch als Anzeigekultur für alle Fäden. Muss vor dem ersten Fenster laufen.
    ///
    /// Schlägt das fehl, läuft die App mit der Kultur des Systems weiter: eine ungewohnte
    /// Zahlendarstellung ist ärgerlich, ein Absturz beim Start wäre schlimmer.
    /// </summary>
    public static void Apply()
    {
        try
        {
            var deutsch = CultureInfo.GetCultureInfo("de-DE");

            CultureInfo.DefaultThreadCurrentCulture = deutsch;
            CultureInfo.DefaultThreadCurrentUICulture = deutsch;
            CultureInfo.CurrentCulture = deutsch;
            CultureInfo.CurrentUICulture = deutsch;
        }
        catch (CultureNotFoundException)
        {
            // Nur erreichbar, wenn jemand die App ohne Kulturdaten baut
            // (InvariantGlobalization). Dann bleibt es bei der Systemkultur.
        }
    }
}

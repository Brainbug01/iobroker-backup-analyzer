using Avalonia;
using Avalonia.Controls;

namespace IobBackupAnalyzer.Avalonia;

internal static class Program
{
    /// <summary>
    /// Einstiegspunkt der plattformübergreifenden Oberfläche (Windows, macOS, Linux).
    ///
    /// Die Windows-EXE auf WinForms bleibt daneben bestehen und wird weiter gepflegt;
    /// beide teilen sich denselben Core und dieselben Presenter, damit die Analysen in
    /// beiden Oberflächen garantiert dasselbe liefern.
    ///
    /// [STAThread] ist nur unter Windows von Bedeutung (COM-Dialoge), auf den anderen
    /// Plattformen schadet es nicht.
    /// </summary>
    [STAThread]
    public static void Main(string[] args)
    {
        // Deutsche Zahlendarstellung, unabhängig von der Spracheinstellung des Systems.
        // Gerade hier wichtig: Diese Fassung läuft auch auf Linux und macOS, und dort ist
        // Deutsch als Systemsprache eher die Ausnahme. Siehe AppCulture.
        Core.AppCulture.Apply();

        // Selbsttest der Oberfläche, ohne ein Fenster zu zeigen. XAML-Fehler (Tippfehler
        // in Namen, unbekannte Eigenschaften, fehlende Ressourcen) schlagen erst beim
        // Laden zu — der Compiler sieht sie nicht. Gegenstück zum --selftest der
        // WinForms-App. Optional mit Backup-Pfad, dann wird auch das Befüllen geprüft.
        if (args.Length > 0 && args[0] == "--selftest")
        {
            Environment.Exit(RunSelfTest(args.Length > 1 ? args[1] : null));
            return;
        }

        // Bildmodus für die README: je ein Bild pro Tab aus einem geladenen Backup.
        // Kein Anwenderweg — dokumentiert im Abschnitt „Bildschirmfotos" der README.
        if (args.Length > 2 && args[0] == "--screenshots")
        {
            Environment.Exit(Screenshots.Run(args[1], args[2]));
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static int RunSelfTest(string? backupFile)
    {
        try
        {
            // SetupWithoutStarting initialisiert Avalonia vollständig, startet aber
            // keine Ereignisschleife — genau das, was ein Konstruktions-Selbsttest braucht.
            BuildAvaloniaApp().SetupWithoutStarting();

            var window = new MainWindow(null);

            if (backupFile is not null)
            {
                // Befüllt alle portierten Ansichten. Das prüft mehr als die reine
                // Konstruktion: Spaltenbindungen, Zeilenklassen und Presenter-Aufrufe
                // laufen hier mit echten Daten durch.
                var data = Core.BackupLoader.Load(backupFile);
                window.SetData(data);
                Console.WriteLine($"Selbsttest: {backupFile} geladen ({data.Kind}), " +
                                  "alle portierten Ansichten befüllt.");
            }

            Console.WriteLine("Selbsttest bestanden: Fenster und Ansichten konstruieren sauber.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Selbsttest FEHLGESCHLAGEN:");
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    /// <summary>
    /// Wird auch vom Avalonia-Designer erwartet — deshalb als eigene Methode.
    /// UsePlatformDetect wählt das passende Backend: Win32, macOS (Avalonia.Native)
    /// oder X11/Wayland.
    /// </summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
                  .UsePlatformDetect()
                  .WithInterFont()
                  .LogToTrace();
}

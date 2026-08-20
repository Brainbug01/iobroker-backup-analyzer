using System.Text;

namespace IobBackupAnalyzer.App;

internal static class Program
{
    /// <summary>Fehlerprotokoll neben der EXE.</summary>
    public static string ErrorLogPath => Path.Combine(AppContext.BaseDirectory, "error.log");

    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Kein Stacktrace im UI — der geht in die error.log.
        Application.ThreadException += (_, e) => HandleFatal(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) HandleFatal(ex);
        };

        // Konstruktions-Selbsttest der UI-schweren Fenster (ohne echte Anzeige). Fängt genau
        // die Klasse von Fehlern ab, die kein Core-Verify sieht — z. B. eine ungültige
        // SplitContainer-Konfiguration, die erst beim Erzeugen des Controls wirft.
        if (args.Length > 0 && args[0] == "--selftest")
        {
            Environment.Exit(RunSelfTest());
            return;
        }

        // Bildmodus für die README: je ein Bild pro Tab aus einem geladenen Backup.
        // Kein Anwenderweg — dokumentiert im Abschnitt „Bildschirmfotos" der README.
        if (args.Length > 2 && args[0] == "--screenshots")
        {
            Environment.Exit(Screenshots.Run(args[1], args[2]));
            return;
        }

        var startFile = args.Length > 0 && File.Exists(args[0]) ? args[0] : null;
        Application.Run(new MainForm(startFile));
    }

    /// <summary>
    /// Erzeugt die kritischen Fenster/Tabs samt Layout und meldet, ob dabei eine Exception
    /// fliegt. Rückgabe 0 = alles konstruiert sauber, 1 = Fehler (Details in error.log).
    /// </summary>
    private static int RunSelfTest()
    {
        try
        {
            using (var dlg = new CleanupScriptDialog(new (string, IReadOnlyList<string>)[]
                   {
                       ("beispiel-adapter.0", new[] { "beispiel-adapter.0.a", "beispiel-adapter.0.b" }),
                       ("anderer-adapter.0", new[] { "anderer-adapter.0.x" })
                   }))
            {
                dlg.Size = new Size(940, 560);
                dlg.CreateControl();     // erzwingt Handles + Layout -> SizeChanged der Splitter
            }

            using (var alias = new AliasTab())
            {
                alias.Size = new Size(1200, 800);
                alias.CreateControl();
            }

            using (var main = new MainForm(null))
            {
                main.Size = new Size(1400, 900);
                main.CreateControl();
            }

            return 0;
        }
        catch (Exception ex)
        {
            LogError("Selbsttest fehlgeschlagen", ex);
            return 1;
        }
    }

    private static void HandleFatal(Exception ex)
    {
        LogError("Unbehandelter Fehler", ex);
        MessageBox.Show(
            "Es ist ein unerwarteter Fehler aufgetreten.\r\n\r\n" +
            ex.Message + "\r\n\r\n" +
            "Details wurden protokolliert in:\r\n" + ErrorLogPath,
            "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    /// <summary>Schreibt einen Fehler samt Stacktrace in die error.log.</summary>
    public static void LogError(string context, Exception ex)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine(new string('=', 70));
            sb.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {context}");
            sb.AppendLine(ex.ToString());
            File.AppendAllText(ErrorLogPath, sb.ToString(), Encoding.UTF8);
        }
        catch (Exception logEx) when (logEx is IOException or UnauthorizedAccessException)
        {
            // Wenn nicht einmal das Protokoll schreibbar ist, hilft nur noch die MessageBox.
        }
    }
}

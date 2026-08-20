using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using IobBackupAnalyzer.Core;

namespace IobBackupAnalyzer.Avalonia;

/// <summary>
/// Schreibt je ein Bild pro Tab — das Gegenstück zum Bildmodus der Windows-Fassung.
///
/// Anders als dort wird hier nicht vom Bildschirm abfotografiert, sondern der visuelle
/// Baum selbst gerendert (<see cref="RenderTargetBitmap"/>). Das kommt ohne
/// System.Drawing aus und liefert auf jeder Plattform dasselbe Bild.
///
/// Aufruf: <c>ioBroker-Backup-Analyzer --screenshots &lt;backup&gt; &lt;zielordner&gt;</c>
/// </summary>
internal static class Screenshots
{
    /// <summary>
    /// Ausgangsgröße vor dem Maximieren. Aufgenommen wird maximiert — wie in der
    /// Windows-Fassung, damit die Bilder beider Oberflächen dieselben Maße haben und
    /// sich in der README nebeneinander vergleichen lassen.
    /// </summary>
    private const int Breite = 1500;
    private const int Hoehe = 950;

    /// <summary>
    /// Tabs ohne Bild: Hilfe und Änderungsverlauf sind reiner Fließtext, der ohnehin in
    /// der README und im Verlauf steht.
    /// </summary>
    private static readonly string[] OhneBild = { "Hilfe", "Änderungen" };

    public static int Run(string backupPfad, string zielOrdner)
    {
        if (!File.Exists(backupPfad))
        {
            Console.Error.WriteLine($"Backup nicht gefunden: {backupPfad}");
            return 2;
        }

        Directory.CreateDirectory(zielOrdner);

        // Wie beim Selbsttest: Avalonia vollständig einrichten, aber ohne Ereignisschleife.
        // Was sonst die Schleife erledigt, stößt Dispatcher.RunJobs von Hand an.
        Program.BuildAvaloniaApp().SetupWithoutStarting();

        var window = new MainWindow(null) { Width = Breite, Height = Hoehe };
        window.Show();
        window.WindowState = WindowState.Maximized;
        Arbeiten();

        window.ApplyDataForScreenshots(BackupLoader.Load(backupPfad), backupPfad);
        Arbeiten();

        var tabs = window.FindControl<TabControl>("Tabs");
        if (tabs is null)
        {
            Console.Error.WriteLine("TabControl „Tabs\" nicht gefunden.");
            return 1;
        }

        var koepfe = tabs.Items.OfType<TabItem>().Select(t => t.Header?.ToString() ?? "").ToList();
        var geschrieben = 0;

        for (var i = 0; i < koepfe.Count; i++)
        {
            if (OhneBild.Contains(koepfe[i])) continue;

            tabs.SelectedIndex = i;
            Arbeiten();

            // Erste Zeile wählen, damit die Detailtabelle darunter nicht leer bleibt.
            ErsteZeileWaehlen(tabs);
            Arbeiten();

            var name = Dateiname(i, koepfe[i]);
            var ziel = Path.Combine(zielOrdner, name);

            // Maße und Skalierung vom Fenster selbst nehmen: Erst dann entsteht ein Bild
            // in genau der Auflösung, in der das Fenster auch auf dem Bildschirm steht.
            var groesse = window.ClientSize;
            var massstab = window.RenderScaling;
            var pixel = new PixelSize((int)Math.Round(groesse.Width * massstab),
                                      (int)Math.Round(groesse.Height * massstab));

            window.Measure(groesse);
            window.Arrange(new Rect(groesse));
            Arbeiten();

            using (var bmp = new RenderTargetBitmap(pixel, new Vector(96 * massstab, 96 * massstab)))
            {
                bmp.Render(window);
                bmp.Save(ziel);
            }

            Console.WriteLine($"  {name}");
            geschrieben++;
        }

        Console.WriteLine($"{geschrieben} Bilder in {zielOrdner}");
        return 0;
    }

    /// <summary>
    /// Sucht das erste gefüllte <see cref="DataGrid"/> im sichtbaren Tab und wählt dort
    /// die erste Zeile.
    /// </summary>
    private static bool ErsteZeileWaehlen(Visual wurzel)
    {
        foreach (var kind in wurzel.GetVisualDescendants().OfType<DataGrid>())
        {
            if (kind.SelectedIndex >= 0) continue;

            var hatZeilen = kind.ItemsSource is System.Collections.IEnumerable quelle
                            && quelle.GetEnumerator().MoveNext();
            if (!hatZeilen) continue;

            kind.SelectedIndex = 0;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Arbeitet ab, was ohne laufende Ereignisschleife liegen bleibt: Layout, Bindungen,
    /// nachgezogene Spaltenbreiten. Mehrere Runden, weil das Befüllen einer Tabelle
    /// wiederum Folgearbeiten auslöst.
    /// </summary>
    private static void Arbeiten(int runden = 5)
    {
        for (var i = 0; i < runden; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(40);
        }
    }

    /// <summary>
    /// Aus „VIS-Datenpunkte" wird „05-vis-datenpunkte.png" — dieselbe Benennung wie in der
    /// Windows-Fassung, damit sich die Bilder beider Oberflächen paarweise zuordnen lassen.
    /// </summary>
    private static string Dateiname(int index, string tabTitel)
    {
        var rein = tabTitel.ToLowerInvariant()
                           .Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("ß", "ss");

        var sb = new System.Text.StringBuilder();
        foreach (var c in rein)
        {
            if (char.IsLetterOrDigit(c)) sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }

        return $"{index + 1:00}-{sb.ToString().Trim('-')}.png";
    }
}

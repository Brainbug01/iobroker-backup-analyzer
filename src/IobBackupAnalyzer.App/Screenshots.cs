using System.Drawing.Imaging;
using IobBackupAnalyzer.Core;

namespace IobBackupAnalyzer.App;

/// <summary>
/// Schreibt je ein Bild pro Tab — die Vorlage für die Bildschirmfotos in der README.
///
/// <b>Warum im Programm und nicht von Hand?</b> Bilder von Hand zu schneiden ist bei
/// jeder Fassung wieder Handarbeit, und was dabei zufällig auf dem Bildschirm steht,
/// landet mit im Bild. Hier entsteht jedes Bild aus einem geladenen Backup heraus,
/// reproduzierbar und in immer derselben Größe.
///
/// Aufruf: <c>ioBroker-Backup-Analyzer.exe --screenshots &lt;backup&gt; &lt;zielordner&gt;</c>
/// </summary>
internal static class Screenshots
{
    /// <summary>
    /// Ausgangsgröße vor dem Maximieren. Aufgenommen wird maximiert: Eine feste Größe in
    /// Pixeln gibt es unter Windows nicht — bei 125 % Skalierung wird aus einem
    /// 1500er-Fenster ein 1936 Pixel breites, und das ragt auf einem 1920er Bildschirm
    /// rechts hinaus. Maximiert passt das Fenster immer genau auf den Arbeitsbereich.
    /// </summary>
    private static readonly Size Fenster = new(1500, 950);

    /// <summary>
    /// Tabs, von denen kein Bild entsteht: Hilfe und Änderungsverlauf sind reiner
    /// Fließtext, der ohnehin in der README und im Verlauf steht. Ein Bild davon zeigt
    /// nichts, was die Oberfläche kann.
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

        var daten = BackupLoader.Load(backupPfad);

        // Das Fenster wird wirklich angezeigt. DrawToBitmap auf ein nie dargestelltes
        // Formular liefert nur den Rahmen: Die Steuerelemente haben dann nie ein Paint
        // bekommen und bleiben leer. Aufgenommen wird deshalb vom Bildschirm — das ist
        // zugleich genau das, was ein Anwender zu sehen bekommt.
        using var form = new MainForm(null);
        form.Size = Fenster;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(0, 0);
        form.TopMost = true;
        form.Show();
        form.WindowState = FormWindowState.Maximized;
        form.Activate();
        form.ApplyDataForScreenshots(daten, backupPfad);
        Warten();

        var tabs = form.TabsForScreenshots;
        var geschrieben = 0;

        for (var i = 0; i < tabs.TabPages.Count; i++)
        {
            if (OhneBild.Contains(tabs.TabPages[i].Text)) continue;

            tabs.SelectedIndex = i;

            // Erst zeichnen lassen, dann aufnehmen: Spaltenbreiten und Zeilen stehen erst
            // nach einem vollständigen Durchlauf der Ereignisschleife fest.
            form.Refresh();
            Warten();

            // In den Tabs mit Haupt- und Detailliste die erste Zeile auswählen. Ohne
            // Auswahl bleibt die untere Hälfte leer, und gerade das Zusammenspiel beider
            // Listen ist das, was ein Bild zeigen soll.
            ErsteZeileWaehlen(tabs.TabPages[i]);
            Warten(3);

            var name = Dateiname(i, tabs.TabPages[i].Text);
            var ziel = Path.Combine(zielOrdner, name);

            // Bounds statt Size/Location: Maximiert weichen beide voneinander ab, und
            // aufgenommen werden soll genau der Bereich, den das Fenster einnimmt.
            //
            // Der Schnitt mit dem Bildschirm ist nötig, weil ein maximiertes Fenster unter
            // Windows um die Rahmenbreite über jeden Rand hinausragt. Ohne ihn stünde an
            // den Kanten des Bildes der Desktop statt des Fensters.
            var rahmen = Rectangle.Intersect(form.Bounds, Screen.FromControl(form).Bounds);
            using var bmp = new Bitmap(rahmen.Width, rahmen.Height);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(rahmen.Location, Point.Empty, rahmen.Size);
            }
            bmp.Save(ziel, ImageFormat.Png);

            Console.WriteLine($"  {name}");
            geschrieben++;
        }

        form.Hide();

        Console.WriteLine($"{geschrieben} Bilder in {zielOrdner}");
        return 0;
    }

    /// <summary>
    /// Wählt in der ersten gefüllten Liste des Tabs die erste Zeile. Die Reihenfolge der
    /// Steuerelemente entspricht der Anzeige von oben nach unten, getroffen wird also die
    /// Hauptliste — die Detailliste darunter füllt sich daraufhin von selbst.
    /// </summary>
    private static bool ErsteZeileWaehlen(Control wurzel)
    {
        foreach (Control c in wurzel.Controls)
        {
            if (c is ListView lv && lv.Items.Count > 0 && lv.SelectedItems.Count == 0)
            {
                lv.Items[0].Selected = true;
                lv.Items[0].Focused = true;
                lv.EnsureVisible(0);
                return true;
            }

            if (ErsteZeileWaehlen(c)) return true;
        }

        return false;
    }

    /// <summary>
    /// Lässt die Ereignisschleife arbeiten, bis nichts mehr ansteht. Ein einzelnes
    /// <c>DoEvents</c> reicht nicht: Das Befüllen einer Liste stößt Folgearbeiten an
    /// (Spaltenbreiten, Sortierung), die erst im nächsten Durchlauf abgearbeitet werden.
    /// </summary>
    private static void Warten(int runden = 6)
    {
        for (var i = 0; i < runden; i++)
        {
            Application.DoEvents();
            Thread.Sleep(60);
        }
    }

    /// <summary>
    /// Aus „VIS-Datenpunkte" wird „03-vis-datenpunkte.png". Die laufende Nummer hält die
    /// Reihenfolge der Oberfläche fest, damit die Bilder im Ordner so liegen wie die Tabs.
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

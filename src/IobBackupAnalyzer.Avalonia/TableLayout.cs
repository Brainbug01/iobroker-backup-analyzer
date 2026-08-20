using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace IobBackupAnalyzer.Avalonia;

/// <summary>
/// Gibt der letzten Spalte jeder Tabelle den freien Platz rechts — und zwar als feste
/// Breite in Pixeln, nicht als dehnbare Sternbreite.
///
/// <b>Warum nicht einfach <c>Width="*"</c>?</b> Zwei Anläufe, beide nachgemessen:
/// Eine Sternspalte schrumpft, bis alles in den Rahmen passt. Das DataGrid meldet dann
/// nie eine Überbreite und blendet den waagerechten Rollbalken aus — die hinteren
/// Spalten sind schlicht nicht mehr erreichbar. Steht die Sternspalte am Ende, quetscht
/// Avalonia stattdessen die Spalten davor auf Strichbreite zusammen. Beides ist
/// schlimmer als eine leere Fläche rechts.
///
/// Feste Breiten lösen das, hinterlassen aber genau diese leere Fläche, sobald das
/// Fenster breiter ist als die Summe der Spalten. Deshalb wird hier bei jeder
/// Größenänderung ausgerechnet, wie breit die letzte Spalte sein muss, damit sie bündig
/// mit dem Tabellenrand abschließt. Ist das Fenster zu schmal, fällt sie auf ihre
/// Ausgangsbreite zurück — und weil dann alle Breiten fest sind, erscheint der
/// waagerechte Rollbalken wie gewünscht.
/// </summary>
internal static class TableLayout
{
    /// <summary>
    /// Rahmen und Rundungsreserve. Bewusst klein: Den Platz des senkrechten Rollbalkens
    /// deckt bereits die Abschlussspalte ab — würde er hier ein zweites Mal abgezogen,
    /// bliebe rechts neben der Abschlussspalte ein weiterer leerer Streifen stehen, und
    /// die Tabelle sähe aus, als hätte sie zwei leere Spalten am Ende.
    ///
    /// Ganz ohne Reserve geht es aber auch nicht: Bei krummen Breiten wäre die Tabelle
    /// sonst um Bruchteile eines Pixels zu breit, und der waagerechte Rollbalken
    /// erschiene, obwohl alles hineinpasst.
    /// </summary>
    private const double BorderAndRounding = 4;

    /// <summary>
    /// Untergrenze je letzter Spalte. Muss gemerkt werden, bevor die erste Berechnung
    /// sie überschreibt.
    /// </summary>
    private static readonly ConditionalWeakTable<DataGridColumn, object> Baseline = new();

    /// <summary>
    /// Die in der XAML angegebene Breite, gemerkt bevor irgendetwas sie überschreibt.
    ///
    /// Sie ist die harte Untergrenze. Ohne sie kann die Messung danebengreifen: Läuft sie,
    /// bevor die Zeilen gezeichnet sind, misst sie nur die Überschrift — die Spalte
    /// schrumpft dann auf wenige Pixel und verschwindet praktisch (einmal passiert,
    /// deshalb steht das hier).
    /// </summary>
    private static readonly ConditionalWeakTable<DataGridColumn, object> Original = new();

    /// <summary>
    /// Hängt die Berechnung an alle Tabellen unterhalb von <paramref name="root"/>.
    /// Eine Zeile je Ansicht, damit keine Tabelle vergessen wird.
    /// </summary>
    public static void FillLastColumn(Control root)
    {
        foreach (var grid in root.GetLogicalDescendants().OfType<DataGrid>())
            Attach(grid);
    }

    /// <summary>
    /// Breite der leeren Abschlussspalte. Sie muss den senkrechten Rollbalken überdecken
    /// können, sonst erfüllt sie ihren Zweck nicht.
    /// </summary>
    private const double SpacerWidth = 24;

    /// <summary>Die Abschlussspalte je Tabelle — sie zählt bei der Füllrechnung nicht mit.</summary>
    private static readonly ConditionalWeakTable<DataGrid, DataGridColumn> Spacers = new();

    private static void Attach(DataGrid grid)
    {
        foreach (var c in grid.Columns)
            if (c.Width.IsAbsolute && c.Width.Value > 0)
                Original.AddOrUpdate(c, c.Width.Value);

        // Leere Abschlussspalte ganz rechts.
        //
        // Sie löst ein Ärgernis, das sonst nicht wegzubekommen ist: Der senkrechte
        // Rollbalken liegt über dem rechten Rand des Inhalts, und der Rollbereich endet
        // vor ihm. Die letzte Spalte war dadurch selbst am Anschlag noch angeschnitten.
        // Jetzt endet der Rollweg an dieser leeren Spalte — sie verschwindet unter dem
        // Balken, und die letzte echte Spalte steht vollständig da.
        var spacer = new DataGridTextColumn
        {
            Header = string.Empty,
            Width = new DataGridLength(SpacerWidth),
            CanUserSort = false,
            CanUserResize = false,
            CanUserReorder = false,
            IsReadOnly = true
        };
        grid.Columns.Add(spacer);
        Spacers.AddOrUpdate(grid, spacer);

        // Die Breite steht erst nach dem Layout fest; SizeChanged deckt beides ab —
        // den ersten Aufbau und jede spätere Änderung der Fenstergröße.
        grid.SizeChanged += (_, _) => Apply(grid);

        // Rechtsklick auf einen Spaltenkopf passt genau diese Spalte an ihren Inhalt an.
        // Tunnel, weil das DataGrid den Klick sonst selbst verarbeitet (Auswahl, Sortieren).
        grid.AddHandler(InputElement.PointerPressedEvent, OnHeaderRightClick,
                        RoutingStrategies.Tunnel);

        // Nach dem Laden eines Backups steht die Tabelle neu da — dann noch einmal rechnen,
        // sonst behält die letzte Spalte die Breite aus der leeren Ansicht.
        grid.PropertyChanged += (_, e) =>
        {
            if (e.Property == DataGrid.ItemsSourceProperty)
                Dispatcher.UIThread.Post(() => Apply(grid), DispatcherPriority.Background);
        };
    }

    /// <summary>
    /// Rechtsklick auf einen Spaltenkopf: Die Spalte wird so breit, dass ihr längster
    /// sichtbarer Text hineinpasst — und nur diese, nicht die ganze Tabelle.
    ///
    /// Die Spalte wird über ihre Überschrift zugeordnet. Der direkte Weg (die Spalte am
    /// Kopf selbst erfragen) ist in Avalonia nicht öffentlich; die Überschriften sind
    /// innerhalb einer Tabelle ohnehin eindeutig.
    /// </summary>
    private static void OnHeaderRightClick(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (!e.GetCurrentPoint(grid).Properties.IsRightButtonPressed) return;
        if (e.Source is not Visual source) return;

        var header = source as DataGridColumnHeader
                     ?? source.FindAncestorOfType<DataGridColumnHeader>();
        if (header is null) return;

        var column = grid.Columns.FirstOrDefault(c => Equals(c.Header, header.Content));
        if (column is null) return;

        // Die Abschlussspalte hat keinen Inhalt, den man messen könnte.
        if (Spacers.TryGetValue(grid, out var spacer) && ReferenceEquals(column, spacer)) return;

        e.Handled = true;
        FitToContent(grid, column);
    }

    /// <summary>
    /// Misst die Spalte am Inhalt und schreibt das Ergebnis als feste Breite fest.
    ///
    /// Das Festschreiben ist wichtig: Eine Spalte, die auf "am Inhalt messen" stehen
    /// bleibt, verändert ihre Breite beim Rollen wieder — gemessen wird nämlich nur, was
    /// gerade gezeichnet ist. Einmal messen, dann stehen lassen.
    /// </summary>
    private static void FitToContent(DataGrid grid, DataGridColumn column)
    {
        column.Width = DataGridLength.Auto;

        Dispatcher.UIThread.Post(() =>
        {
            var measured = column.ActualWidth;
            if (measured <= 0) return;

            column.Width = new DataGridLength(measured);

            // Ist es die letzte Spalte mit Inhalt, gilt die neue Breite auch als deren
            // Untergrenze — sonst zöge die Füllrechnung sie sofort wieder auf den alten
            // Wert zurück.
            Original.AddOrUpdate(column, measured);
            Baseline.Remove(column);

            Apply(grid);
        }, DispatcherPriority.Background);
    }

    private static void Apply(DataGrid grid)
    {
        var columns = grid.Columns.Where(c => c.IsVisible).ToList();

        // Die Abschlussspalte bleibt außen vor: Sie soll schmal bleiben und nicht den
        // freien Platz schlucken — füllen soll die letzte Spalte mit Inhalt.
        var spacerWidth = 0.0;
        if (Spacers.TryGetValue(grid, out var spacer))
        {
            columns.Remove(spacer);
            spacerWidth = spacer.ActualWidth > 0 ? spacer.ActualWidth : SpacerWidth;
        }

        if (columns.Count < 2) return;

        var last = columns[^1];
        if (!Baseline.TryGetValue(last, out var stored))
        {
            // Untergrenze ist die in der XAML angegebene Breite.
            //
            // Ein Versuch, sie stattdessen am längsten Text zu messen (Width=Auto, Breite
            // ablesen, festschreiben), ist verworfen: Die Messung lief mal vor dem ersten
            // Zeichnen der Zeilen und lieferte dann die Breite der Überschrift — die
            // Spalte schrumpfte auf ein paar Pixel oder verschwand ganz. Eine gut
            // gewählte Zahl in der XAML ist verlässlicher als eine Messung, deren
            // Zeitpunkt man nicht sicher trifft.
            var floor = Original.TryGetValue(last, out var orig) ? (double)orig : last.MinWidth;
            if (floor <= 0) floor = last.ActualWidth;
            if (floor <= 0) return;   // Layout noch nicht gelaufen — beim nächsten Mal
            stored = floor;
            Baseline.Add(last, stored);
        }

        var min = (double)stored;
        var others = columns.Take(columns.Count - 1).Sum(c => c.ActualWidth);
        var available = grid.Bounds.Width - BorderAndRounding - spacerWidth - others;
        var target = Math.Max(min, available);

        // Ohne diese Schwelle setzt jede Zuweisung ein neues Layout in Gang, das erneut
        // SizeChanged auslöst — die Tabelle würde endlos zittern.
        if (Math.Abs(last.ActualWidth - target) > 1)
            last.Width = new DataGridLength(target);
    }
}

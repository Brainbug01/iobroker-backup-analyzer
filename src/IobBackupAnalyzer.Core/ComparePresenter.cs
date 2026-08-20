namespace IobBackupAnalyzer.Core;

/// <summary>
/// Eine Zeile der Vergleichsanzeige. <paramref name="IsGap"/> markiert die
/// Zusammenfassung ausgelassener unveränderter Zeilen.
/// </summary>
public sealed record DiffDisplayLine(string Old, string New, string Marker, string Text,
                                     DiffKind Kind, bool IsGap);

/// <summary>
/// UI-neutrale Logik des Vergleich-Tabs: Kopftexte, Spalten, Zeilenaufbereitung und die
/// Auswahl der sichtbaren Diff-Zeilen. Geteilt von WinForms- und Avalonia-Oberfläche.
/// </summary>
public static class ComparePresenter
{
    /// <summary>Obergrenze der dargestellten Diff-Zeilen — darüber wird die Anzeige zäh.</summary>
    public const int MaxDiffLines = 4000;

    /// <summary>Unveränderte Zeilen rund um eine Änderung, wenn nur Änderungen gezeigt werden.</summary>
    public const int ContextLines = 3;

    // ------------------------------------------------------------------ Spalten

    public static readonly string[] MetricColumns = { "Kennzahl", "Vorher", "Nachher", "Differenz" };

    public static readonly string[] InstanceColumns =
        { "Instanz", "Änderung", "Version", "Aktiviert", "Details" };
    public static readonly string[] InstanceCsvColumns =
        { "Instanz", "Änderung", "Version vorher", "Version nachher", "Details" };

    public static readonly string[] ScriptColumns = { "Skript", "Typ", "Änderung", "Details" };
    public static readonly string[] ScriptCsvColumns =
        { "Skript", "ID", "Typ", "Änderung", "Zeilen neu", "Zeilen entfernt", "Details" };

    public static readonly string[] NamespaceColumns = { "Namensraum", "Neu", "Entfernt", "Saldo" };
    public static readonly string[] ObjectIdColumns = { "Änderung", "Objekt-ID" };
    public static readonly string[] ObjectCsvColumns = { "Namensraum", "Änderung", "Objekt-ID" };

    public static readonly string[] ViewColumns = { "VIS", "View", "Änderung", "Widgets", "Details" };
    public static readonly string[] ViewCsvColumns =
        { "VIS", "View", "Änderung", "Widgets vorher", "Widgets nachher", "Details" };

    // ------------------------------------------------------------------ Kopftexte

    public static string KindText(BackupKind kind) =>
        kind == BackupKind.Full ? "Voll-Backup" : "Skript-Backup";

    /// <summary>Die Zeile über der Auswahl: welches Backup ist gerade geladen?</summary>
    public static string LoadedText(BackupData data) =>
        $"Geladenes Backup:  {Path.GetFileName(data.SourceFile)}   ·   {KindText(data.Kind)}   ·   " +
        $"vom {data.CreatedAt:dd.MM.yyyy HH:mm}";

    /// <summary>
    /// Zwei Zeilen Zusammenfassung: welche Dateien, welcher Abstand, wie viel hat sich
    /// geändert. Zeilentrenner ist <c>\n</c>.
    /// </summary>
    public static string SummaryText(BackupComparison c)
    {
        var span = c.Span;
        var spanText = span is null
            ? ""
            : span.Value.TotalDays >= 1
                ? $"   ·   {span.Value.TotalDays:F0} Tage dazwischen"
                : $"   ·   {span.Value.TotalHours:F0} Stunden dazwischen";

        return $"Vorher:  {Path.GetFileName(c.Before.SourceFile)}  ({c.Before.CreatedAt:dd.MM.yyyy HH:mm})" +
               $"      Nachher:  {Path.GetFileName(c.After.SourceFile)}  ({c.After.CreatedAt:dd.MM.yyyy HH:mm})" +
               spanText + "\n" +
               (c.IsIdentical
                   ? "Keine Unterschiede gefunden — die beiden Stände sind in allen geprüften Punkten gleich."
                   : $"{c.ChangedInstances} Instanzen  ·  {c.ChangedScripts} Skripte  ·  " +
                     $"{c.AddedObjects:N0} Objekte neu / {c.RemovedObjects:N0} entfernt  ·  {c.ChangedViews} Views geändert") +
               (c.OrderUncertain
                   ? "\nHinweis: Die Backup-Zeitpunkte ließen keine Reihenfolge erkennen — es gilt die Ladereihenfolge."
                   : "");
    }

    /// <summary>Platzhaltertext, solange kein zweites Backup gewählt wurde.</summary>
    public static string PlaceholderText(BackupData? loaded) =>
        loaded is null
            ? "Kein Backup geladen.\n\nBitte oben eine Datei öffnen oder hineinziehen."
            : "Zweites Backup zum Vergleich auswählen oder hineinziehen.\n\n" +
              $"Verglichen wird gegen:  {Path.GetFileName(loaded.SourceFile)}";

    /// <summary>
    /// Meldung, wenn ein Skript-Backup gegen ein Voll-Backup gestellt würde. Das ergäbe
    /// lauter Scheinänderungen — jedes Objekt und jede Instanz fehlte auf einer Seite.
    /// </summary>
    public static string NotComparableText(BackupKind loaded, BackupKind other) =>
        "Die beiden Dateien sind von unterschiedlicher Art:\n\n" +
        $"    geladen:    {KindText(loaded)}\n" +
        $"    ausgewählt: {KindText(other)}\n\n" +
        "Vergleichbar sind nur zwei Voll-Backups oder zwei Skript-Backups.";

    /// <summary>
    /// Rückfragetext, wenn die Backups aus verschiedenen ioBroker-Installationen stammen.
    /// Der Vergleich bleibt möglich — bei einem Systemumzug ist er sogar das Gewollte —,
    /// aber er soll nie unbemerkt passieren.
    /// </summary>
    public static string DifferentSystemText(SystemIdentity mine, SystemIdentity other)
    {
        var hasIds = mine.InstallationId.Length > 0 && other.InstallationId.Length > 0;

        return "Die beiden Backups stammen aus verschiedenen ioBroker-Installationen.\n\n" +
               $"    geladen:     {mine.Describe()}\n" +
               $"    ausgewählt:  {other.Describe()}\n\n" +
               (hasIds
                   ? "Die Installations-IDs (system.meta.uuid) sind verschieden — das ist eindeutig.\n\n"
                   : "Erkannt an Hostname und Adresse; eine Installations-ID liegt nicht vor.\n\n") +
               "Ein Vergleich ist trotzdem möglich und bei einem Systemumzug oder dem Abgleich " +
               "zweier Anlagen auch sinnvoll. Rechne aber mit sehr vielen Unterschieden, die " +
               "keine Änderung sind, sondern schlicht der andere Bestand.\n\n" +
               "Trotzdem vergleichen?";
    }

    // ------------------------------------------------------------------ Kennzahlen

    public static string[] Row(MetricRow m) =>
        new[] { m.Label, m.Before.ToString("N0"), m.After.ToString("N0"), m.DeltaText };

    /// <summary>
    /// Zuwachs grün, Rückgang rot. Die WinForms-Fassung färbt nur die Differenzspalte,
    /// die Avalonia-Fassung die ganze Zeile — inhaltlich dieselbe Aussage.
    /// </summary>
    public static RowEmphasis Emphasis(MetricRow m) =>
        m.Delta > 0 ? RowEmphasis.Positive : m.Delta < 0 ? RowEmphasis.Problem : RowEmphasis.None;

    // ------------------------------------------------------------------ Instanzen

    public static List<T> OnlyChanged<T>(IEnumerable<T> rows, bool onlyChanged, Func<T, ChangeKind> kind) =>
        onlyChanged ? rows.Where(r => kind(r) != ChangeKind.Unchanged).ToList() : rows.ToList();

    public static List<InstanceChange> FilterInstances(BackupComparison c, bool onlyChanged) =>
        OnlyChanged(c.Instances, onlyChanged, i => i.Kind);

    /// <summary>„Ja", „Nein" oder „Ja → Nein" — die Logik liegt am Modell.</summary>
    public static string EnabledText(InstanceChange i) => i.EnabledDisplay;

    public static string[] DisplayRow(InstanceChange i) =>
        new[] { i.Namespace, i.KindText, i.VersionText, EnabledText(i), i.Detail };

    public static string[] Row(InstanceChange i) =>
        new[] { i.Namespace, i.KindText, i.VersionBefore, i.VersionAfter, i.Detail };

    /// <summary>
    /// Ein Downgrade ist der Fall, den man beim Fehlersuchen sucht — er wird deutlicher
    /// markiert als ein gewöhnliches Update.
    /// </summary>
    public static RowEmphasis Emphasis(InstanceChange i) => i.Kind switch
    {
        ChangeKind.Added => RowEmphasis.Positive,
        ChangeKind.Removed => RowEmphasis.Problem,
        ChangeKind.Changed => i.VersionDirection > 0 ? RowEmphasis.Warn : RowEmphasis.None,
        _ => RowEmphasis.Muted
    };

    // ------------------------------------------------------------------ Skripte

    public static List<ScriptChange> FilterScripts(BackupComparison c, bool onlyChanged) =>
        OnlyChanged(c.Scripts, onlyChanged, s => s.Kind);

    public static string[] DisplayRow(ScriptChange s) =>
        new[] { s.DisplayPath, s.EngineText, s.KindText, s.Detail };

    public static string[] Row(ScriptChange s) =>
        new[] { s.DisplayPath, s.Id, s.EngineText, s.KindText,
                s.AddedLines.ToString(), s.RemovedLines.ToString(), s.Detail };

    public static RowEmphasis Emphasis(ScriptChange s) => s.Kind switch
    {
        ChangeKind.Added => RowEmphasis.Positive,
        ChangeKind.Removed => RowEmphasis.Problem,
        ChangeKind.Unchanged => RowEmphasis.Muted,
        _ => RowEmphasis.None
    };

    /// <summary>Woran der Vergleich ansetzt — bei Blockly am XML, sonst am Quelltext.</summary>
    public static string DiffBasis(ScriptChange sc) =>
        (sc.After ?? sc.Before)!.Engine == ScriptEngine.Blockly
            ? "Verglichen wird das Blockly-XML"
            : "Verglichen wird der Quelltext";

    public static string DiffInfoText(ScriptChange sc, DiffResult result) =>
        $"{DiffBasis(sc)}   ·   +{result.Added} / −{result.Removed} Zeilen" +
        (result.Truncated ? "   ·   zu groß für einen zeilengenauen Vergleich" : "");

    /// <summary>
    /// Wählt die anzuzeigenden Diff-Zeilen aus. Bei <paramref name="onlyChanged"/> bleiben
    /// nur Änderungen samt <see cref="ContextLines"/> Zeilen Umfeld übrig; ausgelassene
    /// Bereiche werden zu einer Lückenzeile zusammengefasst. Über
    /// <see cref="MaxDiffLines"/> hinaus wird abgeschnitten.
    /// </summary>
    public static List<DiffDisplayLine> VisibleLines(DiffResult result, bool onlyChanged)
    {
        var lines = result.Lines;
        var show = new bool[lines.Count];

        if (onlyChanged)
        {
            for (var i = 0; i < lines.Count; i++)
            {
                if (lines[i].Kind == DiffKind.Unchanged) continue;
                for (var j = Math.Max(0, i - ContextLines); j <= Math.Min(lines.Count - 1, i + ContextLines); j++)
                    show[j] = true;
            }
        }
        else
        {
            Array.Fill(show, true);
        }

        var output = new List<DiffDisplayLine>();
        var gap = false;

        for (var i = 0; i < lines.Count; i++)
        {
            if (!show[i]) { gap = true; continue; }

            if (gap)
            {
                output.Add(new DiffDisplayLine("", "", "", "⋯", DiffKind.Unchanged, true));
                gap = false;
            }

            if (output.Count(l => !l.IsGap) >= MaxDiffLines)
            {
                output.Add(new DiffDisplayLine("", "", "",
                    $"… weitere {lines.Count - i} Zeilen ausgeblendet.", DiffKind.Unchanged, true));
                break;
            }

            var l = lines[i];
            output.Add(new DiffDisplayLine(
                l.OldLine?.ToString() ?? "", l.NewLine?.ToString() ?? "",
                l.Marker, l.Text, l.Kind, false));
        }

        return output;
    }

    // ------------------------------------------------------------------ Objekte

    public static string[] Row(NamespaceChange n) =>
        new[] { n.Namespace, n.Added.ToString("N0"), n.Removed.ToString("N0"), n.DeltaText };

    public static RowEmphasis Emphasis(NamespaceChange n) =>
        n.Delta > 0 ? RowEmphasis.Positive : n.Delta < 0 ? RowEmphasis.Problem : RowEmphasis.None;

    /// <summary>Die Objekt-IDs eines Namensraums: erst die neuen, dann die entfernten.</summary>
    public static List<(string Change, string Id, bool IsAdded)> ObjectIds(NamespaceChange n)
    {
        var rows = new List<(string, string, bool)>();
        foreach (var id in n.AddedIds) rows.Add(("neu", id, true));
        foreach (var id in n.RemovedIds) rows.Add(("entfernt", id, false));
        return rows;
    }

    public static List<string[]> ObjectCsvRows(BackupComparison c)
    {
        var rows = new List<string[]>();
        foreach (var n in c.Namespaces)
        {
            foreach (var id in n.AddedIds) rows.Add(new[] { n.Namespace, "neu", id });
            foreach (var id in n.RemovedIds) rows.Add(new[] { n.Namespace, "entfernt", id });
        }
        return rows;
    }

    // ------------------------------------------------------------------ Views

    public static List<ViewChange> FilterViews(BackupComparison c, bool onlyChanged) =>
        OnlyChanged(c.Views, onlyChanged, v => v.Kind);

    /// <summary>Widgetzahl; bei geänderter Anzahl als „12 → 15" — die Logik liegt am Modell.</summary>
    public static string WidgetsText(ViewChange v) => v.WidgetsDisplay;

    public static string[] DisplayRow(ViewChange v) =>
        new[] { v.VersionText, v.View, v.KindText, WidgetsText(v), v.Detail };

    public static string[] Row(ViewChange v) =>
        new[] { v.VersionText, v.View, v.KindText,
                v.WidgetsBefore.ToString(), v.WidgetsAfter.ToString(), v.Detail };

    public static RowEmphasis Emphasis(ViewChange v) => v.Kind switch
    {
        ChangeKind.Added => RowEmphasis.Positive,
        ChangeKind.Removed => RowEmphasis.Problem,
        ChangeKind.Unchanged => RowEmphasis.Muted,
        _ => RowEmphasis.None
    };
}

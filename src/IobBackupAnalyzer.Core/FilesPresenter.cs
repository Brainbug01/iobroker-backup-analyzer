namespace IobBackupAnalyzer.Core;

/// <summary>
/// Aufbereitung der Dateiliste für beide Oberflächen: Filtern, Sortieren, Beschriftungen.
/// Enthält keine Oberflächenbezüge, damit WinForms und Avalonia dieselbe Liste zeigen.
/// </summary>
public static class FilesPresenter
{
    public static readonly string[] Columns = { "Namensraum", "Pfad", "Größe", "Typ" };

    /// <summary>Eintrag der Namensraum-Auswahl, wenn nicht eingegrenzt werden soll.</summary>
    public const string AllNamespaces = "Alle Namensräume";

    public static string[] Row(BackupFileInfo f) =>
        new[] { f.Namespace, f.Path, FormatSize(f.Size), f.KindText };

    /// <summary>Die Auswahlliste der Namensräume, alphabetisch, mit „Alle" an erster Stelle.</summary>
    public static List<string> NamespaceChoices(IEnumerable<BackupFileInfo> files)
    {
        var list = new List<string> { AllNamespaces };
        list.AddRange(files.Select(f => f.Namespace)
                           .Distinct(StringComparer.OrdinalIgnoreCase)
                           .OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
        return list;
    }

    /// <summary>Filtert nach Namensraum und freiem Text (Pfad oder Dateiname).</summary>
    public static List<BackupFileInfo> Filter(List<BackupFileInfo> all, string? ns, string? text)
    {
        IEnumerable<BackupFileInfo> q = all;

        if (!string.IsNullOrEmpty(ns) && ns != AllNamespaces)
            q = q.Where(f => string.Equals(f.Namespace, ns, StringComparison.OrdinalIgnoreCase));

        var needle = text?.Trim();
        if (!string.IsNullOrEmpty(needle))
            q = q.Where(f => f.DisplayPath.Contains(needle, StringComparison.OrdinalIgnoreCase));

        return q.ToList();
    }

    /// <summary>Sortiert nach Spaltenindex; -1 lässt die Reihenfolge unverändert.</summary>
    public static List<BackupFileInfo> Sort(List<BackupFileInfo> files, int column, bool ascending)
    {
        if (column < 0) return files;

        IOrderedEnumerable<BackupFileInfo> sorted = column switch
        {
            0 => files.OrderBy(f => f.Namespace, StringComparer.OrdinalIgnoreCase)
                      .ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase),
            1 => files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase),
            // Größe als Zahl, nicht als Text — sonst stünde „9 KB" hinter „10 MB".
            2 => files.OrderBy(f => f.Size),
            3 => files.OrderBy(f => f.KindText, StringComparer.OrdinalIgnoreCase)
                      .ThenBy(f => f.DisplayPath, StringComparer.OrdinalIgnoreCase),
            _ => files.OrderBy(f => f.DisplayPath, StringComparer.OrdinalIgnoreCase)
        };

        return ascending ? sorted.ToList() : sorted.Reverse().ToList();
    }

    /// <summary>Kopfzeile über der Liste: Umfang plus das, was das Backup bewusst auslässt.</summary>
    public static string SummaryText(BackupData data)
    {
        if (data.Files.Count == 0)
            return "Dieses Backup enthält keine Dateien aus dem Admin-Dateibereich.";

        var namespaces = data.Files.Select(f => f.Namespace)
                                   .Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var total = data.Files.Sum(f => f.Size);

        return $"{data.Files.Count:N0} Dateien in {namespaces} Namensräumen  ·  zusammen {FormatSize(total)}\n" +
               "Gesichert sind nur deine eigenen Inhalte. Die Dateien der Adapter selbst " +
               "(im Admin die Ordner ohne Instanznummer, z. B. „vis\" oder „echarts\") fehlen " +
               "hier bewusst: Sie gehören dem Adapter und werden beim Wiederherstellen von " +
               "ioBroker selbst wieder angelegt.";
    }

    /// <summary>
    /// Beschriftung des Knopfes, der die ganze Liste exportiert — wortgleich zum
    /// Skripte-Tab (<see cref="ScriptsPresenter.ExportAllLabel"/>): Exportiert wird immer
    /// das, was gerade in der Liste steht.
    /// </summary>
    public static string ExportAllLabel(int shown, int total) =>
        ScriptsPresenter.ExportAllLabel(shown, total);

    public static string CountText(int shown, int total) =>
        shown == total ? $"{total:N0} Dateien" : $"{shown:N0} von {total:N0} Dateien";

    /// <summary>Meldungstext nach einem Export — samt Hinweisen und Fehlern.</summary>
    public static string ExportSummary(BackupFileExporter.ExportResult result)
    {
        var msg = $"{result.Files:N0} Dateien ({FormatSize(result.Bytes)}) exportiert nach:\n{result.RootDir}";

        if (result.Renamed > 0)
            msg += $"\n\n{result.Renamed:N0} Dateinamen mussten angepasst werden: ioBroker erlaubt " +
                   "Zeichen wie den Doppelpunkt, Windows nicht. Sie wurden durch „_\" ersetzt.";

        if (result.Missing.Count > 0)
            msg += $"\n\n{result.Missing.Count:N0} Dateien waren im Archiv nicht auffindbar:\n"
                 + string.Join("\n", result.Missing.Take(5));

        if (result.Errors.Count > 0)
            msg += $"\n\n{result.Errors.Count:N0} Dateien konnten nicht geschrieben werden:\n"
                 + string.Join("\n", result.Errors.Take(5));

        return msg;
    }

    /// <summary>
    /// Größe in der Einheit, die sie lesbar macht. Die Formatierung liegt im Modell, damit
    /// die Avalonia-Tabelle direkt an <see cref="BackupFileInfo.SizeText"/> binden kann.
    /// </summary>
    public static string FormatSize(long bytes) => BackupFileInfo.FormatSize(bytes);
}

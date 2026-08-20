namespace IobBackupAnalyzer.Core;

/// <summary>Welche Logging-Einträge die Liste zeigt.</summary>
public enum LoggingScope
{
    /// <summary>Alle Einträge.</summary>
    All,
    /// <summary>Nur Einträge mit aktivem Logging.</summary>
    ActiveOnly,
    /// <summary>Nur abgeschaltete Einträge — der eigentliche Aufräumfall.</summary>
    DisabledOnly
}

/// <summary>
/// UI-neutrale Logik des Logging-Tabs: Zusammenfassung, Bereichswahl, Filter und die
/// Spaltendefinitionen. Geteilt von WinForms- und Avalonia-Oberfläche.
///
/// In der WinForms-Fassung lag die Filterlogik doppelt vor — einmal zum Füllen der Liste,
/// einmal für den CSV-Export. Hier gibt es sie genau einmal, damit Anzeige und Export
/// nicht auseinanderlaufen können.
/// </summary>
public static class LoggingPresenter
{
    /// <summary>Spalten der Anzeige.</summary>
    public static readonly string[] Columns =
        { "Datenpunkt-ID", "Name", "Instanz", "Adapter", "Aktiv", "Nur bei Änderung",
          "Entprellung", "Alias-Name" };

    /// <summary>
    /// Spalten des CSV-Exports. Unterschied zur Anzeige: Die Entprellung wird als blanke
    /// Millisekundenzahl geschrieben, damit sie sich auswerten lässt.
    /// </summary>
    public static readonly string[] CsvColumns =
        { "Datenpunkt-ID", "Name", "Instanz", "Adapter", "Aktiv", "Nur bei Änderung",
          "Entprellung (ms)", "Alias-Name" };

    /// <summary>Die Beschriftungen der Bereichsauswahl, in der Reihenfolge von <see cref="LoggingScope"/>.</summary>
    public static readonly string[] ScopeLabels =
        { "Alle", "Nur aktives Logging", "Nur deaktiviertes Logging" };

    /// <summary>
    /// Zwei Zeilen Zusammenfassung über alle Einträge; bei leerem Bestand ein erklärender
    /// Satz statt Nullen. Zeilentrenner ist <c>\n</c>.
    /// </summary>
    public static string SummaryText(IReadOnlyList<LoggingRow> all)
    {
        if (all.Count == 0)
            return "Dieses Backup enthält keine Logging-Konfiguration " +
                   "(kein common.custom an den Datenpunkten).";

        var datapoints = all.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count();
        var active = all.Count(r => r.Enabled);

        var byAdapter = string.Join("     ", all
            .GroupBy(r => r.Adapter, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => $"{g.Key}: {g.Count()}"));

        return $"{all.Count} Logging-Einträge     ·     {datapoints} Datenpunkte     ·     " +
               $"{active} aktiv, {all.Count - active} deaktiviert\nNach Instanz —   " + byAdapter;
    }

    /// <summary>
    /// Bereichswahl und Suchbegriff in einem Schritt. Gesucht wird über ID, Instanz,
    /// Adapter und Name — die vier Angaben, nach denen man einen Eintrag wiederfindet.
    /// </summary>
    public static List<LoggingRow> Filter(IEnumerable<LoggingRow> all, LoggingScope scope, string? term)
    {
        IEnumerable<LoggingRow> q = scope switch
        {
            LoggingScope.ActiveOnly => all.Where(r => r.Enabled),
            LoggingScope.DisabledOnly => all.Where(r => !r.Enabled),
            _ => all
        };

        var t = (term ?? "").Trim();
        if (t.Length > 0)
            q = q.Where(r => r.Id.Contains(t, StringComparison.OrdinalIgnoreCase)
                          || r.Instance.Contains(t, StringComparison.OrdinalIgnoreCase)
                          || r.Adapter.Contains(t, StringComparison.OrdinalIgnoreCase)
                          || r.Name.Contains(t, StringComparison.OrdinalIgnoreCase));

        return q.ToList();
    }

    /// <summary>„12 von 40 Einträgen" bzw. „40 Einträge", wenn nicht gefiltert wird.</summary>
    public static string CountText(int shown, int total) =>
        shown == total ? $"{total} Einträge" : $"{shown} von {total} Einträgen";

    /// <summary>Ein Eintrag für die Anzeige, Reihenfolge wie <see cref="Columns"/>.</summary>
    public static string[] DisplayRow(LoggingRow r) =>
        new[] { r.Id, r.Name, r.Instance, r.Adapter, r.EnabledText,
                r.ChangesOnlyText, r.DebounceText, r.AliasId };

    /// <summary>Ein Eintrag für den CSV-Export, Reihenfolge wie <see cref="CsvColumns"/>.</summary>
    public static string[] Row(LoggingRow r) =>
        new[] { r.Id, r.Name, r.Instance, r.Adapter, r.EnabledText, r.ChangesOnlyText,
                r.DebounceMs > 0 ? r.DebounceMs.ToString() : "", r.AliasId };
}

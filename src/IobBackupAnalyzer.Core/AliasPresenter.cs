namespace IobBackupAnalyzer.Core;

/// <summary>Welche Aliasse die Liste zeigt.</summary>
public enum AliasScope
{
    /// <summary>Alle Aliasse mit Ziel.</summary>
    All,
    /// <summary>Nur solche, deren Ziel-Datenpunkt fehlt — die eigentlichen Fehlerfälle.</summary>
    BrokenOnly,
    /// <summary>Nur solche mit getrenntem Lese- und Schreibziel.</summary>
    SplitTargets,
    /// <summary>Nur solche mit Konverterfunktion.</summary>
    WithConverter
}

/// <summary>
/// UI-neutrale Logik des Alias-Tabs: Zusammenfassung, Bereichswahl, Filter und
/// Spaltendefinitionen. Geteilt von WinForms- und Avalonia-Oberfläche.
/// </summary>
public static class AliasPresenter
{
    /// <summary>Spalten der Anzeige.</summary>
    public static readonly string[] Columns =
        { "Alias-ID", "Name", "Lese-Ziel", "Ziel vorhanden", "Schreib-Ziel (abweichend)",
          "Schreib-Ziel vorhanden", "Konverter" };

    /// <summary>
    /// Spalten des CSV-Exports: zusätzlich der Konvertercode selbst, den die Tabelle nur
    /// als „ja/nein" zeigt.
    /// </summary>
    public static readonly string[] CsvColumns =
        { "Alias-ID", "Name", "Lese-Ziel", "Ziel vorhanden", "Schreib-Ziel",
          "Schreib-Ziel vorhanden", "Konverter", "Konverter Lesen", "Konverter Schreiben" };

    /// <summary>Beschriftungen der Bereichsauswahl, in der Reihenfolge von <see cref="AliasScope"/>.</summary>
    public static readonly string[] ScopeLabels =
        { "Alle", "Nur kaputte (Ziel fehlt)", "Nur mit abweichendem Schreibziel", "Nur mit Konverter" };

    /// <summary>Eine Zeile Zusammenfassung; bei leerem Bestand ein erklärender Satz.</summary>
    public static string SummaryText(IReadOnlyList<AliasRow> all)
    {
        if (all.Count == 0) return "Dieses Backup enthält keine Aliasse mit Ziel.";

        var broken = all.Count(a => a.Broken);
        var splitTargets = all.Count(a => !a.SingleTarget);
        var withConv = all.Count(a => a.HasConverter);

        return $"{all.Count} Aliasse     ·     mit fehlendem Ziel: {broken}     ·     " +
               $"mit getrenntem Lese-/Schreibziel: {splitTargets}     ·     mit Konverter: {withConv}";
    }

    /// <summary>
    /// Bereichswahl und Suchbegriff in einem Schritt. Gesucht wird über Alias-ID, Name und
    /// beide Zielangaben.
    /// </summary>
    public static List<AliasRow> Filter(IEnumerable<AliasRow> all, AliasScope scope, string? term)
    {
        IEnumerable<AliasRow> q = scope switch
        {
            AliasScope.BrokenOnly => all.Where(a => a.Broken),
            AliasScope.SplitTargets => all.Where(a => !a.SingleTarget),
            AliasScope.WithConverter => all.Where(a => a.HasConverter),
            _ => all
        };

        var t = (term ?? "").Trim();
        if (t.Length > 0)
            q = q.Where(a => a.Id.Contains(t, StringComparison.OrdinalIgnoreCase)
                          || a.Name.Contains(t, StringComparison.OrdinalIgnoreCase)
                          || a.ReadTarget.Contains(t, StringComparison.OrdinalIgnoreCase)
                          || a.WriteTarget.Contains(t, StringComparison.OrdinalIgnoreCase));

        return q.ToList();
    }

    /// <summary>„12 von 40 Aliassen" bzw. „40 Aliasse", wenn nicht gefiltert wird.</summary>
    public static string CountText(int shown, int total) =>
        shown == total ? $"{total} Aliasse" : $"{shown} von {total} Aliassen";

    /// <summary>Ein Alias für die Anzeige, Reihenfolge wie <see cref="Columns"/>.</summary>
    public static string[] DisplayRow(AliasRow a) =>
        new[] { a.Id, a.Name, a.ReadTarget, a.ReadExistsText,
                a.WriteTargetText, a.WriteExistsText, a.ConverterText };

    /// <summary>
    /// Ein Alias für den CSV-Export. Bei gemeinsamem Lese-/Schreibziel bleiben die beiden
    /// Schreibziel-Spalten leer — sonst stünde die Zielangabe doppelt in der Datei.
    /// </summary>
    public static string[] Row(AliasRow a) =>
        new[] { a.Id, a.Name, a.ReadTarget, a.ReadExistsText,
                a.SingleTarget ? "" : a.WriteTarget,
                a.SingleTarget ? "" : a.WriteExistsText,
                a.ConverterText, a.ConverterRead, a.ConverterWrite };

    /// <summary>Überschrift des Detailbereichs zur ausgewählten Zeile.</summary>
    public static string DetailHeader(AliasRow? a) =>
        a is null ? "Alias-Details — Zeile oben auswählen"
                  : $"Alias-Details: {a.Id}  →  {a.ReadTarget}";
}

namespace IobBackupAnalyzer.Core;

/// <summary>Welche VIS-Datenpunkte die Liste zeigt.</summary>
public enum VisScope
{
    All,
    /// <summary>Nur in VIS 1 verwendet.</summary>
    Vis1Only,
    /// <summary>Nur in VIS 2 verwendet.</summary>
    Vis2Only,
    /// <summary>In beiden VIS-Versionen verwendet.</summary>
    Both,
    /// <summary>Datenpunkt existiert im Backup nicht — das Widget zeigt ins Leere.</summary>
    MissingOnly,
    /// <summary>Nur Aliasse.</summary>
    AliasesOnly,
    /// <summary>Nur Aliasse, deren Ziel fehlt.</summary>
    BrokenAliasesOnly
}

/// <summary>
/// UI-neutrale Logik des VIS-Tabs: Zusammenfassung, Bereichswahl, Filter, Sortierung sowie
/// die Spalten der Datenpunkt- und der Fundstellenliste. Geteilt von WinForms- und
/// Avalonia-Oberfläche.
/// </summary>
public static class VisPresenter
{
    /// <summary>Spalten der Datenpunktliste.</summary>
    public static readonly string[] Columns =
        { "Datenpunkt-ID", "Name", "VIS 1", "VIS 2", "Widgets", "Vorhanden",
          "Alias → Ziel", "Widget-Typen", "Views" };

    /// <summary>Spalten der Fundstellenliste.</summary>
    public static readonly string[] UsageColumns =
        { "VIS", "Projekt", "View", "Widget", "Widget-Typ", "Widget-Set", "Feld", "Zugriff" };

    /// <summary>
    /// Spalten des CSV-Exports: eine Zeile je <b>Fundstelle</b>, nicht je Datenpunkt — so
    /// lässt sich in einer Tabellenkalkulation direkt filtern, welches Widget in welcher
    /// View welchen Datenpunkt wofür nutzt.
    /// </summary>
    public static readonly string[] CsvColumns =
        { "Datenpunkt-ID", "Name", "Vorhanden", "Alias-Ziel", "Ziel fehlt",
          "VIS", "Projekt", "View", "Widget-ID", "Widget-Name", "Widget-Typ", "Widget-Set",
          "Feld", "Zugriff" };

    /// <summary>Beschriftungen der Bereichsauswahl, in der Reihenfolge von <see cref="VisScope"/>.</summary>
    public static readonly string[] ScopeLabels =
        { "Alle", "Nur VIS 1", "Nur VIS 2", "In beiden", "Nur fehlende Datenpunkte",
          "Nur Aliasse", "Nur Aliasse mit fehlendem Ziel" };

    /// <summary>
    /// Zwei Zeilen Zusammenfassung: Kennzahlen und die gefundenen View-Dateien.
    /// Zeilentrenner ist <c>\n</c>. Enthält das Backup keine VIS-Views, steht das
    /// stattdessen als einzelner Satz da.
    /// </summary>
    public static string SummaryText(IReadOnlyList<VisDatapoint> all, BackupData data)
    {
        if (data.VisViews.Count == 0)
            return "Dieses Backup enthält keine VIS-Views (keine vis-views.json gefunden).";

        var v1 = all.Count(d => d.InVis1);
        var v2 = all.Count(d => d.InVis2);
        var both = all.Count(d => d.InVis1 && d.InVis2);
        var missing = all.Count(d => !d.ExistsInBackup);
        var aliases = all.Count(d => d.IsAlias);
        var aliasBroken = all.Count(d => d.AliasTargetMissing);

        var files = string.Join("     ", data.VisViews
            .OrderBy(f => f.Version)
            .Select(f => $"{f.VersionText}: {f.Path}"));

        return $"{all.Count} verwendete Datenpunkte     ·     VIS 1: {v1}     ·     VIS 2: {v2}     ·     " +
               $"in beiden: {both}     ·     ohne existierenden Datenpunkt: {missing}     ·     " +
               $"Aliasse: {aliases}" + (aliasBroken > 0 ? $" (davon {aliasBroken} mit fehlendem Ziel)" : "") +
               $"\n{files}";
    }

    /// <summary>
    /// Bereichswahl und Suchbegriff in einem Schritt. Der Suchbegriff greift bewusst breit —
    /// auf ID, Alias-Ziel, Views, Widget-Typen und jede einzelne Fundstelle —, damit sich
    /// Fragen wie „was steckt in View X?" oder „wo wird Widget-Typ Y benutzt?" beantworten lassen.
    /// </summary>
    public static List<VisDatapoint> Filter(IEnumerable<VisDatapoint> all, VisScope scope, string? term)
    {
        IEnumerable<VisDatapoint> q = scope switch
        {
            VisScope.Vis1Only => all.Where(d => d.InVis1 && !d.InVis2),
            VisScope.Vis2Only => all.Where(d => d.InVis2 && !d.InVis1),
            VisScope.Both => all.Where(d => d.InVis1 && d.InVis2),
            VisScope.MissingOnly => all.Where(d => !d.ExistsInBackup),
            VisScope.AliasesOnly => all.Where(d => d.IsAlias),
            VisScope.BrokenAliasesOnly => all.Where(d => d.AliasTargetMissing),
            _ => all
        };

        var t = (term ?? "").Trim();
        if (t.Length > 0)
            q = q.Where(d => d.Id.Contains(t, StringComparison.OrdinalIgnoreCase)
                          || d.AliasTarget.Contains(t, StringComparison.OrdinalIgnoreCase)
                          || d.ViewsText.Contains(t, StringComparison.OrdinalIgnoreCase)
                          || d.WidgetsText.Contains(t, StringComparison.OrdinalIgnoreCase)
                          || d.Usages.Any(u =>
                                 u.WidgetId.Contains(t, StringComparison.OrdinalIgnoreCase)
                              || u.WidgetName.Contains(t, StringComparison.OrdinalIgnoreCase)
                              || u.Field.Contains(t, StringComparison.OrdinalIgnoreCase)));

        return q.ToList();
    }

    /// <summary>
    /// Sortiert nach Spaltenindex aus <see cref="Columns"/>. Ein unbekannter oder negativer
    /// Index sortiert nach Datenpunkt-ID (Grundzustand).
    /// </summary>
    public static List<VisDatapoint> Sort(IEnumerable<VisDatapoint> points, int column, bool ascending)
    {
        var list = points as IList<VisDatapoint> ?? points.ToList();

        IOrderedEnumerable<VisDatapoint> sorted = column switch
        {
            1 => ascending
                ? list.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                : list.OrderByDescending(d => d.Name, StringComparer.OrdinalIgnoreCase),
            2 => ascending ? list.OrderBy(d => d.InVis1) : list.OrderByDescending(d => d.InVis1),
            3 => ascending ? list.OrderBy(d => d.InVis2) : list.OrderByDescending(d => d.InVis2),
            4 => ascending ? list.OrderBy(d => d.WidgetCount) : list.OrderByDescending(d => d.WidgetCount),
            5 => ascending
                ? list.OrderBy(d => d.ExistsInBackup)
                : list.OrderByDescending(d => d.ExistsInBackup),
            6 => ascending
                ? list.OrderBy(d => d.AliasTarget, StringComparer.OrdinalIgnoreCase)
                : list.OrderByDescending(d => d.AliasTarget, StringComparer.OrdinalIgnoreCase),
            7 => ascending
                ? list.OrderBy(d => d.WidgetsText, StringComparer.OrdinalIgnoreCase)
                : list.OrderByDescending(d => d.WidgetsText, StringComparer.OrdinalIgnoreCase),
            _ => ascending
                ? list.OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
                : list.OrderByDescending(d => d.Id, StringComparer.OrdinalIgnoreCase)
        };

        return sorted.ThenBy(d => d.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>„12 von 40 Datenpunkten" bzw. „40 Datenpunkte", wenn nicht gefiltert wird.</summary>
    public static string CountText(int shown, int total) =>
        shown == total ? $"{total} Datenpunkte" : $"{shown} von {total} Datenpunkten";

    /// <summary>Ein Datenpunkt für die Anzeige, Reihenfolge wie <see cref="Columns"/>.</summary>
    public static string[] DisplayRow(VisDatapoint d) =>
        new[] { d.Id, d.Name, d.Vis1Text, d.Vis2Text, d.WidgetCount.ToString("N0"),
                d.ExistsText, d.AliasTargetText, d.WidgetsText, d.ViewsText };

    /// <summary>
    /// Die Fundstellen eines Datenpunkts in stabiler Reihenfolge: erst VIS-Version,
    /// dann View, dann Widget.
    /// </summary>
    public static List<VisUsage> SortedUsages(VisDatapoint d) =>
        d.Usages
         .OrderBy(u => u.Version)
         .ThenBy(u => u.Project, StringComparer.OrdinalIgnoreCase)
         .ThenBy(u => u.View, StringComparer.OrdinalIgnoreCase)
         .ThenBy(u => u.WidgetId, StringComparer.OrdinalIgnoreCase)
         .ToList();

    /// <summary>Eine Fundstelle für die Anzeige, Reihenfolge wie <see cref="UsageColumns"/>.</summary>
    public static string[] UsageRow(VisUsage u) =>
        new[] { u.VersionText, u.Project, u.View, u.WidgetLabel, u.Template, u.WidgetSet,
                u.Field, u.AttributeText };

    /// <summary>
    /// Überschrift der Fundstellenliste. Enthält die Warnung, wenn der Datenpunkt oder
    /// sein Alias-Ziel gar nicht existiert — dann zeigt das Widget ins Leere.
    /// </summary>
    public static string UsageHeader(VisDatapoint? d, string copyHint)
    {
        if (d is null) return "Fundstellen — Zeile oben auswählen";

        var text = $"Fundstellen von {d.Id}";
        if (d.AliasTarget.Length > 0) text += $"  →  {d.AliasTarget}";
        text += $"   ·   {d.WidgetCount} Widget(s), {d.UsageCount} Verwendung(en)";
        if (copyHint.Length > 0) text += "   ·   " + copyHint;

        if (!d.ExistsInBackup)
            text += "   ·   ⚠ Datenpunkt existiert im Backup nicht — Widget zeigt ins Leere";
        else if (d.AliasTargetMissing)
            text += "   ·   ⚠ Das Alias-Ziel existiert im Backup nicht";

        return text;
    }

    /// <summary>
    /// Die CSV-Zeilen zur aktuell gefilterten Menge — eine je Fundstelle,
    /// Reihenfolge wie <see cref="CsvColumns"/>.
    /// </summary>
    public static List<string[]> CsvRows(IEnumerable<VisDatapoint> points) =>
        points.SelectMany(d => SortedUsages(d).Select(u => new[]
        {
            d.Id, d.Name, d.ExistsText, d.AliasTarget,
            d.AliasTargetMissing ? "Ja" : "", u.VersionText, u.Project, u.View,
            u.WidgetId, u.WidgetName, u.Template, u.WidgetSet, u.Field, u.AttributeText
        })).ToList();

    // ------------------------------------------------- Projekt als ZIP für den Import

    /// <summary>Beschriftung des Knopfes, der ein VIS-Projekt als ZIP schreibt.</summary>
    public const string ZipButtonLabel = "Projekt als ZIP (VIS-Import)";

    /// <summary>Beschriftung des Schalters, der Bilder und CSS mit in die ZIP nimmt.</summary>
    public const string ZipAssetsLabel = "mit Bildern und CSS";

    /// <summary>Auswahleintrag, wenn das Backup keine VIS-Projekte enthält.</summary>
    public const string NoProjects = "Keine VIS-Projekte im Backup";

    /// <summary>
    /// Zählwort in der richtigen Form: „1 Datei", „5 Dateien". Klammerformen wie
    /// „Datei(en)" lesen sich in genau dem Fall schlecht, der am häufigsten vorkommt —
    /// nämlich bei genau einem Stück.
    /// </summary>
    public static string Count(int n, string singular, string plural) =>
        n == 1 ? $"1 {singular}" : $"{n:N0} {plural}";

    /// <summary>
    /// Der Weg vom ZIP zurück in die Anlage. Steht in der Erfolgsmeldung und in der Hilfe,
    /// deshalb nur einmal hier.
    ///
    /// Zum Projektnamen, an VIS 1.5.6 nachgemessen: Das Feld im Dialog wirkt, wird beim
    /// Hineinziehen der Datei aber <b>überschrieben</b> — VIS trägt dort den Dateinamen
    /// ohne führendes Datum ein. Ein vorher getippter Name ist danach weg. Deshalb steht
    /// die Reihenfolge im Text: erst ziehen, dann benennen.
    /// </summary>
    public const string ImportHint =
        "So kommt das Projekt zurück in die Anlage:\n" +
        "1. In VIS: Tools → Projektimport\n" +
        "2. Die ZIP-Datei in das Feld ziehen. VIS trägt den Projektnamen daraufhin\n" +
        "   selbst ein — den Dateinamen ohne führendes Datum.\n" +
        "3. Erst JETZT den Projektnamen prüfen und bei Bedarf ändern; ein vorher\n" +
        "   getippter Name wird vom Hineinziehen überschrieben. Niemals den Namen des\n" +
        "   laufenden Projekts stehen lassen — der Import ersetzt ein gleichnamiges\n" +
        "   Projekt vollständig. Dann „Import projekt“.\n" +
        "4. In das neue Projekt wechseln, die gesuchte View über „Views → Exportieren“\n" +
        "   herausholen und im eigenen Projekt über „Views → Importieren“ einfügen.\n" +
        "5. Aufräumen: Tools → Setup → Dateimanager, dort den Ordner des\n" +
        "   Wiederherstellungs-Projekts unter vis.0 bzw. vis-2.0 löschen.";

    /// <summary>
    /// Zeile vor der Projektauswahl. Bewusst kurz: In der Windows-Fassung steht dafür nur
    /// der Platz links neben der Auswahl, und wozu die ZIP dient, sagen der Knopf daneben
    /// und die Hilfe.
    /// </summary>
    public static string ZipIntro(int projectCount) =>
        projectCount == 0 ? NoProjects : Count(projectCount, "VIS-Projekt", "VIS-Projekte") + " im Backup:";

    /// <summary>Meldungstext nach dem ZIP-Export, samt Anleitung für den Import.</summary>
    public static string ZipSummary(BackupData data, VisProjectExporter.VisProject project,
                                    VisProjectExporter.ZipResult result)
    {
        if (result.Files == 0)
        {
            var text = $"Für „{project.Name}“ konnte nichts geschrieben werden.";
            if (result.Errors.Count > 0) text += "\n\n" + string.Join("\n", result.Errors.Take(5));
            return text;
        }

        var msg = $"{project.VersionText} · Projekt „{project.Name}“: {Count(result.Files, "Datei", "Dateien")} " +
                  $"({BackupFileInfo.FormatSize(result.Bytes)}) gepackt nach:\n{result.ZipPath}\n" +
                  $"Die ZIP-Datei ist {BackupFileInfo.FormatSize(result.ZipBytes)} groß.";

        if (!result.ViewsIncluded)
            msg += $"\n\n⚠ Die {VisProjectExporter.ViewsFileName} fehlt — ohne sie kann VIS die " +
                   "Datei nicht als Projekt einlesen.";

        if (result.Missing.Count > 0)
            msg += $"\n\n{Count(result.Missing.Count, "Datei war", "Dateien waren")} im Archiv nicht auffindbar:\n"
                 + string.Join("\n", result.Missing.Take(5));

        if (result.Errors.Count > 0)
            msg += $"\n\n{Count(result.Errors.Count, "Datei konnte", "Dateien konnten")} nicht gepackt werden:\n"
                 + string.Join("\n", result.Errors.Take(5));

        // Bilderordner neben dem Projekt (vis.0/kamerabilder) sind bewusst nicht dabei —
        // sie gehören dem Namensraum, nicht dem Projekt. Auf derselben Anlage bleiben sie
        // liegen, auf einer fremden fehlen sie.
        var siblings = VisProjectExporter.SiblingFolders(data, project);
        if (siblings.Count > 0)
        {
            var names = string.Join(", ", siblings.Take(5).Select(s => $"{project.Namespace}/{s}"));
            var one = siblings.Count == 1;

            msg += $"\n\nNicht in der ZIP: {names} — {(one ? "dieser Ordner liegt" : "diese Ordner liegen")} "
                 + $"neben dem Projekt, nicht darin. Auf dieser Anlage {(one ? "bleibt er" : "bleiben sie")} "
                 + $"liegen, hier ist nichts zu tun. Erst beim Umzug auf eine andere Anlage "
                 + $"{(one ? "muss er" : "müssen sie")} dort ebenfalls vorhanden sein — "
                 + $"{(one ? "herausholen lässt er sich" : "herausholen lassen sie sich")} im Tab „Dateien“.";
        }

        return msg + "\n\n" + ImportHint;
    }
}

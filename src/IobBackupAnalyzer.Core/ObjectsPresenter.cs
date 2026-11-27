using System.Text.RegularExpressions;

namespace IobBackupAnalyzer.Core;

/// <summary>
/// UI-neutrale Logik des Tabs „Objekte": Suche nach ID-Muster, Typfilter, Auswahl samt
/// Unterobjekten, Zurückhalten heikler Objekte und die Texte für den Export-Dialog.
/// Geteilt von WinForms, Avalonia und der Browser-Fassung.
/// </summary>
public static class ObjectsPresenter
{
    public static readonly string[] Columns = { "ID", "Typ", "Name", "Zurückgehalten" };

    public const string PatternHint =
        "ID-Anfang oder Muster mit * und ?, z. B. echarts.0.* oder 0_userdata.0.Heizung. " +
        "Ohne Stern zählt der Anfang der ID. Groß- und Kleinschreibung spielt bei der Suche " +
        "keine Rolle; exportiert wird die ID genau so, wie sie im Backup steht.";

    public const string IncludeChildrenLabel = "Unterobjekte einschließen";

    public const string IncludeChildrenHint =
        "Exportiert zu jedem angehakten Objekt alle Objekte darunter mit — zu einem Ordner " +
        "oder Gerät also die Kanäle und Datenpunkte, auch wenn sie nicht in der Trefferliste stehen.";

    public const string AllowGuardedLabel = "Zurückgehaltene zulassen";

    public const string AllowGuardedHint =
        "Zurückgehalten werden Objekte mit Zugangsdaten (Benutzer, Zertifikate, Zugangsdaten-" +
        "Ablage, Systemschlüssel, native-Felder wie password oder token) und Adapter-, " +
        "Instanz- und Host-Objekte, die man nicht über „Objekte hochladen\" zurückspielt. " +
        "Eine Importdatei wird leicht weitergegeben — darum nur bewusst freischalten.";

    /// <summary>Warnung, wenn „Zurückgehaltene zulassen" eingeschaltet wird.</summary>
    public const string AllowGuardedWarning =
        "Zurückgehaltene Objekte in den Export aufnehmen?\n\n" +
        "Sie können Passwörter, Schlüssel und Token enthalten — teils verschlüsselt, teils im " +
        "Klartext. Die Datei sollte dann nicht weitergegeben werden.\n\n" +
        "Adapter-, Instanz- und Host-Objekte über „Objekte hochladen\" zurückzuspielen, " +
        "überschreibt Version und Einstellungen einer installierten Instanz. Instanzen werden " +
        "über Backitup wiederhergestellt, nicht über einzelne Objekte.";

    /// <summary>Die Typauswahl: „Alle", dann die im Backup vorkommenden Typen, häufigste zuerst.</summary>
    public static string[] TypeLabels(BackupData? data)
    {
        if (data is null) return new[] { "Alle" };
        return new[] { "Alle" }
            .Concat(data.Objects.Where(o => o.Type.Length > 0)
                                .GroupBy(o => o.Type, StringComparer.Ordinal)
                                .OrderByDescending(g => g.Count())
                                .ThenBy(g => g.Key, StringComparer.Ordinal)
                                .Select(g => g.Key))
            .ToArray();
    }

    /// <summary>
    /// Filtert nach Muster und Typ. <paramref name="type"/> null, leer oder „Alle" lässt jeden
    /// Typ durch. Ein leeres Muster liefert alles — die Tabelle zeigt dann den ganzen Bestand.
    /// </summary>
    public static List<IobObject> Filter(IEnumerable<IobObject> objects, string? pattern, string? type)
    {
        var q = objects;

        if (!string.IsNullOrEmpty(type) && type != "Alle")
            q = q.Where(o => string.Equals(o.Type, type, StringComparison.Ordinal));

        var p = (pattern ?? "").Trim();
        if (p.Length > 0)
        {
            if (p.Contains('*') || p.Contains('?'))
            {
                var regex = new Regex(
                    "^" + Regex.Escape(p).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexLimits.MatchTimeout);
                q = q.Where(o =>
                {
                    try { return regex.IsMatch(o.Id); }
                    catch (RegexMatchTimeoutException) { return false; }
                });
            }
            else
            {
                q = q.Where(o => o.Id.StartsWith(p, StringComparison.OrdinalIgnoreCase));
            }
        }

        return q.OrderBy(o => o.Id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(o => o.Id, StringComparer.Ordinal)
                .ToList();
    }

    public static string[] Row(IobObject o) =>
        new[] { o.Id, o.Type, o.Name, ObjectExportGuard.Reason(o) ?? "" };

    public static RowEmphasis Emphasis(IobObject o) =>
        ObjectExportGuard.Reason(o) is null ? RowEmphasis.None : RowEmphasis.Muted;

    /// <summary>Was tatsächlich exportiert wird — und was davon zurückbleibt.</summary>
    /// <param name="Export">Die IDs für den Exporter.</param>
    /// <param name="Children">Davon über „Unterobjekte einschließen" hinzugekommen.</param>
    /// <param name="Guarded">Zurückgehalten, weil „Zurückgehaltene zulassen" aus ist.</param>
    public sealed record Selection(IReadOnlyList<string> Export, int Children, IReadOnlyList<string> Guarded);

    /// <summary>
    /// Bestimmt die Exportmenge aus den angehakten IDs. Unterobjekte sind alle Objekte, deren
    /// ID mit <c>&lt;id&gt;.</c> beginnt — exakt, wie ioBroker IDs vergleicht.
    /// </summary>
    public static Selection Resolve(BackupData data, IEnumerable<string> checkedIds,
                                    bool includeChildren, bool allowGuarded)
    {
        var byId = new Dictionary<string, IobObject>(StringComparer.Ordinal);
        foreach (var o in data.Objects) byId[o.Id] = o;

        var chosen = new HashSet<string>(checkedIds.Where(byId.ContainsKey), StringComparer.Ordinal);
        var direct = chosen.Count;

        if (includeChildren && chosen.Count > 0)
        {
            // Sortiert und per Binärsuche: Bei „alle Treffer" mit Tausenden IDs wäre der
            // Vergleich jeder ID mit jeder sonst quadratisch.
            var sorted = byId.Keys.ToArray();
            Array.Sort(sorted, StringComparer.Ordinal);
            foreach (var id in chosen.ToList())
            {
                var prefix = id + ".";
                var i = Array.BinarySearch(sorted, prefix, StringComparer.Ordinal);
                if (i < 0) i = ~i;
                for (; i < sorted.Length && sorted[i].StartsWith(prefix, StringComparison.Ordinal); i++)
                    chosen.Add(sorted[i]);
            }
        }
        var children = chosen.Count - direct;

        var guarded = new List<string>();
        var export = new List<string>();
        foreach (var id in chosen.OrderBy(i => i, StringComparer.OrdinalIgnoreCase))
        {
            if (!allowGuarded && ObjectExportGuard.Reason(byId[id]) is not null) guarded.Add(id);
            else export.Add(id);
        }

        return new Selection(export, children, guarded);
    }

    public static string CountText(int shown, int total, int checkedCount)
    {
        var text = shown == total ? $"{total:N0} Objekte" : $"{shown:N0} von {total:N0} Objekten";
        if (checkedCount > 0) text += $"   ·   {checkedCount:N0} angehakt";
        return text;
    }

    /// <summary>Der Text des Bestätigungsdialogs vor Export oder Kopieren.</summary>
    public static string ConfirmText(Selection s)
    {
        var lines = new List<string>
        {
            s.Export.Count == 1 ? "1 Objekt wird exportiert" : $"{s.Export.Count:N0} Objekte werden exportiert"
        };
        if (s.Children > 0)
            lines[0] += $" (davon {s.Children:N0} Unterobjekte)";
        lines[0] += ".";

        if (s.Guarded.Count > 0)
            lines.Add($"{s.Guarded.Count:N0} zurückgehalten (Zugangsdaten oder Adapter/Instanz): "
                      + string.Join(", ", s.Guarded.Take(5)) + (s.Guarded.Count > 5 ? ", …" : ""));

        lines.Add("");
        lines.Add("Bitte beachten:");
        lines.Add("• Der Import im Admin (Objekte → Expertenmodus → „Objekte hochladen\") " +
                  "überschreibt vorhandene Objekte mit gleicher ID ohne Rückfrage.");
        lines.Add("• Wiederhergestellt werden nur die Objekte — keine Werte (States). " +
                  "Datenpunkte starten ohne Wert oder mit ihrem Vorgabewert.");
        lines.Add("• Adapter-Instanzen bitte nicht so zurückspielen, sondern über Backitup.");
        return string.Join("\n", lines);
    }

    /// <summary>Rückmeldung nach dem Export.</summary>
    public static string DoneText(ObjectExporter.Result r, string where)
    {
        var text = $"{r.Count:N0} Objekte {where}.";
        if (r.Missing.Count > 0)
            text += $" {r.Missing.Count:N0} nicht in der Quelle gefunden: " +
                    string.Join(", ", r.Missing.Take(5)) + (r.Missing.Count > 5 ? ", …" : "");
        return text;
    }

    /// <summary>Beschriftung des Knopfes im Vergleich-Tab.</summary>
    public const string CompareExportLabel = "Verschwundene als Import-JSON exportieren";

    public const string CompareExportHint =
        "Schreibt die Objekte, die im neueren Backup fehlen, aus dem älteren Backup in eine " +
        "Datei für „Objekte hochladen\" im Admin — der Weg, versehentlich gelöschte Objekte " +
        "zurückzuholen. Zurückgehaltene Objekte (Zugangsdaten, Adapter/Instanzen) bleiben draußen.";

    /// <summary>
    /// Die verschwundenen Objekte eines Vergleichs — aus dem älteren Backup, ohne Zurückgehaltene.
    /// </summary>
    public static Selection RemovedSelection(BackupComparison cmp) =>
        Resolve(cmp.Before, cmp.Namespaces.SelectMany(n => n.RemovedIds), includeChildren: false, allowGuarded: false);

    /// <summary>Dateiname für den Export aus dem Vergleich.</summary>
    public static string RemovedFileName(BackupComparison cmp) =>
        ObjectExporter.FileName(cmp.Before, "verschwunden");
}

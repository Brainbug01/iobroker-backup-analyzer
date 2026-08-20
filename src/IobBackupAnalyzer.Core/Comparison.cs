namespace IobBackupAnalyzer.Core;

/// <summary>Art einer Änderung zwischen zwei Backups.</summary>
public enum ChangeKind
{
    /// <summary>Im neueren Backup vorhanden, im älteren nicht.</summary>
    Added,
    /// <summary>Im älteren Backup vorhanden, im neueren nicht mehr.</summary>
    Removed,
    /// <summary>In beiden vorhanden, aber inhaltlich verschieden.</summary>
    Changed,
    /// <summary>In beiden identisch.</summary>
    Unchanged
}

public static class ChangeKindText
{
    public static string Of(ChangeKind k) => k switch
    {
        ChangeKind.Added => "neu",
        ChangeKind.Removed => "entfernt",
        ChangeKind.Changed => "geändert",
        _ => "unverändert"
    };
}

/// <summary>Eine Kennzahl im Vorher-Nachher-Vergleich.</summary>
public sealed class MetricRow
{
    public required string Label { get; init; }
    public required int Before { get; init; }
    public required int After { get; init; }

    public int Delta => After - Before;

    /// <summary>Differenz mit Vorzeichen; leer, wenn sich nichts geändert hat.</summary>
    public string DeltaText => Delta == 0 ? "" : Delta > 0 ? $"+{Delta:N0}" : $"{Delta:N0}";
}

/// <summary>Eine Adapter-Instanz im Vergleich zweier Backups.</summary>
public sealed class InstanceChange
{
    public required string Namespace { get; init; }
    public required string Adapter { get; init; }
    public required ChangeKind Kind { get; init; }

    public string VersionBefore { get; init; } = "";
    public string VersionAfter { get; init; } = "";
    public bool? EnabledBefore { get; init; }
    public bool? EnabledAfter { get; init; }
    public int ObjectsBefore { get; init; }
    public int ObjectsAfter { get; init; }

    public bool VersionChanged =>
        VersionBefore.Length > 0 && VersionAfter.Length > 0 && VersionBefore != VersionAfter;

    public bool EnabledChanged =>
        EnabledBefore is not null && EnabledAfter is not null && EnabledBefore != EnabledAfter;

    /// <summary>
    /// Ob die Version gestiegen oder gefallen ist. Ein Rückgang ist praxisrelevant: er
    /// bedeutet ein Downgrade, das eine Fehlersuche erklären kann.
    /// </summary>
    public int VersionDirection => VersionChanged ? CompareVersions(VersionBefore, VersionAfter) : 0;

    public string KindText => ChangeKindText.Of(Kind);

    /// <summary>
    /// Der Aktiv-Status für die Anzeige: „Ja", „Nein" oder „Ja → Nein", je nachdem was
    /// sich geändert hat. Liegt am Modell, damit beide Oberflächen direkt daran binden.
    /// </summary>
    public string EnabledDisplay => Kind switch
    {
        ChangeKind.Added => EnabledAfter == true ? "Ja" : "Nein",
        ChangeKind.Removed => EnabledBefore == true ? "Ja" : "Nein",
        _ => EnabledChanged
            ? $"{(EnabledBefore == true ? "Ja" : "Nein")}  →  {(EnabledAfter == true ? "Ja" : "Nein")}"
            : EnabledAfter == true ? "Ja" : "Nein"
    };

    public string VersionText => Kind switch
    {
        ChangeKind.Added => VersionAfter,
        ChangeKind.Removed => VersionBefore,
        _ => VersionChanged ? $"{VersionBefore}  →  {VersionAfter}" : VersionAfter
    };

    public string Detail
    {
        get
        {
            if (Kind == ChangeKind.Added) return $"Instanz neu angelegt ({ObjectsAfter:N0} Objekte)";
            if (Kind == ChangeKind.Removed) return $"Instanz entfernt ({ObjectsBefore:N0} Objekte)";

            var parts = new List<string>();
            if (VersionChanged)
                parts.Add(VersionDirection < 0 ? "Update" : VersionDirection > 0 ? "Downgrade" : "Version geändert");
            if (EnabledChanged)
                parts.Add(EnabledAfter == true ? "aktiviert" : "deaktiviert");
            if (ObjectsAfter != ObjectsBefore)
                parts.Add($"Objekte {ObjectsBefore:N0} → {ObjectsAfter:N0}");

            return parts.Count == 0 ? "unverändert" : string.Join(", ", parts);
        }
    }

    /// <summary>
    /// Vergleicht zwei Versionsangaben segmentweise numerisch (1.10.0 ist neuer als 1.9.0,
    /// was ein reiner Textvergleich falsch beurteilen würde). Rückgabe wie bei
    /// <see cref="string.CompareTo(string)"/>: negativ = die neuere Fassung ist höher.
    /// </summary>
    private static int CompareVersions(string before, string after)
    {
        var a = Split(before);
        var b = Split(after);

        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var x = i < a.Length ? a[i] : 0;
            var y = i < b.Length ? b[i] : 0;
            if (x != y) return x < y ? -1 : 1;
        }

        // Gleiche Zahlen, aber unterschiedlicher Text (z. B. Vorabversionen wie 3.0.0-beta.1).
        return string.CompareOrdinal(before, after) switch { < 0 => -1, > 0 => 1, _ => 0 };

        static int[] Split(string v) => v.Split('.', '-', '+')
            .Select(p => int.TryParse(p, out var n) ? n : 0)
            .ToArray();
    }
}

/// <summary>Ein Skript im Vergleich zweier Backups.</summary>
public sealed class ScriptChange
{
    public required string Id { get; init; }
    public required string DisplayPath { get; init; }
    public required ChangeKind Kind { get; init; }

    public ScriptInfo? Before { get; init; }
    public ScriptInfo? After { get; init; }

    public int AddedLines { get; init; }
    public int RemovedLines { get; init; }

    /// <summary>true, wenn sich nur der Aktiv-Status geändert hat, nicht der Inhalt.</summary>
    public bool OnlyStatusChanged => Kind == ChangeKind.Changed && AddedLines == 0 && RemovedLines == 0;

    public bool EnabledChanged =>
        Before is not null && After is not null && Before.Enabled != After.Enabled;

    public string KindText => ChangeKindText.Of(Kind);

    public string EngineText => (After ?? Before)?.EngineText ?? "";

    public string Detail
    {
        get
        {
            if (Kind == ChangeKind.Added) return "neu angelegt";
            if (Kind == ChangeKind.Removed) return "gelöscht";

            var parts = new List<string>();
            if (AddedLines > 0 || RemovedLines > 0) parts.Add($"+{AddedLines} / −{RemovedLines} Zeilen");
            if (EnabledChanged) parts.Add(After!.Enabled ? "aktiviert" : "deaktiviert");
            return parts.Count == 0 ? "unverändert" : string.Join(", ", parts);
        }
    }

    /// <summary>
    /// Der Text, der verglichen wird: bei Blockly das XML (das ist die eigentliche Quelle),
    /// sonst der JavaScript-Code ohne den angehängten Base64-Block.
    /// </summary>
    public static string ComparableText(ScriptInfo s) =>
        s.Engine == ScriptEngine.Blockly && s.BlocklyXml is not null ? s.BlocklyXml : s.CleanSource;
}

/// <summary>Objektänderungen, zusammengefasst je Namensraum.</summary>
public sealed class NamespaceChange
{
    public required string Namespace { get; init; }
    public List<string> AddedIds { get; init; } = new();
    public List<string> RemovedIds { get; init; } = new();

    public int Added => AddedIds.Count;
    public int Removed => RemovedIds.Count;
    public int Delta => Added - Removed;

    public string DeltaText => Delta == 0 ? "±0" : Delta > 0 ? $"+{Delta:N0}" : $"{Delta:N0}";
}

/// <summary>Eine VIS-View im Vergleich zweier Backups.</summary>
public sealed class ViewChange
{
    public required VisVersion Version { get; init; }
    public required string View { get; init; }
    public required ChangeKind Kind { get; init; }

    public int WidgetsBefore { get; init; }
    public int WidgetsAfter { get; init; }
    public int WidgetsAdded { get; init; }
    public int WidgetsRemoved { get; init; }
    public int WidgetsChanged { get; init; }

    public string VersionText => Version == VisVersion.Vis1 ? "VIS 1" : "VIS 2";
    public string KindText => ChangeKindText.Of(Kind);

    /// <summary>
    /// Die Widgetzahl für die Anzeige; bei geänderter Anzahl als „12 → 15".
    /// Liegt am Modell, damit beide Oberflächen direkt daran binden.
    /// </summary>
    public string WidgetsDisplay => Kind switch
    {
        ChangeKind.Added => WidgetsAfter.ToString(),
        ChangeKind.Removed => WidgetsBefore.ToString(),
        _ => WidgetsBefore == WidgetsAfter
            ? WidgetsAfter.ToString()
            : $"{WidgetsBefore} → {WidgetsAfter}"
    };

    public string Detail => Kind switch
    {
        ChangeKind.Added => $"View neu ({WidgetsAfter} Widgets)",
        ChangeKind.Removed => $"View entfernt ({WidgetsBefore} Widgets)",
        ChangeKind.Changed => string.Join(", ", Parts()),
        _ => "unverändert"
    };

    private IEnumerable<string> Parts()
    {
        if (WidgetsAdded > 0) yield return $"{WidgetsAdded} Widgets neu";
        if (WidgetsRemoved > 0) yield return $"{WidgetsRemoved} Widgets entfernt";
        if (WidgetsChanged > 0) yield return $"{WidgetsChanged} Widgets geändert";
    }
}

/// <summary>Gesamtergebnis eines Backup-Vergleichs.</summary>
public sealed class BackupComparison
{
    /// <summary>Das ältere der beiden Backups — der Bezugsstand („vorher").</summary>
    public required BackupData Before { get; init; }

    /// <summary>Das neuere der beiden Backups („nachher").</summary>
    public required BackupData After { get; init; }

    /// <summary>
    /// true, wenn die Reihenfolge nicht aus den Backup-Zeitpunkten bestimmt werden konnte
    /// und stattdessen die Ladereihenfolge gilt.
    /// </summary>
    public bool OrderUncertain { get; init; }

    /// <summary>Ergebnis des Herkunftsabgleichs beider Backups.</summary>
    public SystemMatch SystemMatch { get; init; }

    /// <summary>Klartext zum Herkunftsabgleich, für die Anzeige über dem Ergebnis.</summary>
    public string SystemMatchText { get; init; } = "";

    public List<MetricRow> Metrics { get; init; } = new();
    public List<InstanceChange> Instances { get; init; } = new();
    public List<ScriptChange> Scripts { get; init; } = new();
    public List<NamespaceChange> Namespaces { get; init; } = new();
    public List<ViewChange> Views { get; init; } = new();

    public TimeSpan? Span => Before.CreatedAt is { } a && After.CreatedAt is { } b ? b - a : null;

    public int ChangedInstances => Instances.Count(i => i.Kind != ChangeKind.Unchanged);
    public int ChangedScripts => Scripts.Count(s => s.Kind != ChangeKind.Unchanged);
    public int ChangedViews => Views.Count(v => v.Kind != ChangeKind.Unchanged);
    public int AddedObjects => Namespaces.Sum(n => n.Added);
    public int RemovedObjects => Namespaces.Sum(n => n.Removed);

    /// <summary>true, wenn zwischen beiden Ständen überhaupt nichts passiert ist.</summary>
    public bool IsIdentical =>
        ChangedInstances == 0 && ChangedScripts == 0 && ChangedViews == 0
        && AddedObjects == 0 && RemovedObjects == 0;
}

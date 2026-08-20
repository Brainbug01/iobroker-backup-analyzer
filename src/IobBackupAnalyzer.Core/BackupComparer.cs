using System.Text.Json;

namespace IobBackupAnalyzer.Core;

/// <summary>
/// Stellt zwei Backups gegenüber und beantwortet die Frage, die nach einem Update oder
/// einer nächtlichen Änderung wirklich zählt: Was ist zwischen diesen beiden Ständen
/// passiert?
///
/// Verglichen werden Adapter-Instanzen, Skripte (inhaltlich, nicht nur ihre Existenz),
/// der Objektbestand je Namensraum und die VIS-Views. Alle Vergleiche laufen case-sensitiv,
/// weil ioBroker-IDs es sind.
/// </summary>
public static class BackupComparer
{
    public static Task<BackupComparison> CompareAsync(BackupData a, BackupData b,
                                                      CancellationToken ct = default)
        => Task.Run(() => Compare(a, b, ct), ct);

    public static BackupComparison Compare(BackupData a, BackupData b, CancellationToken ct = default)
    {
        // Die Reihenfolge ergibt sich aus dem Backup-Zeitpunkt, nicht aus der Ladereihenfolge —
        // sonst hinge „vorher/nachher" davon ab, welche Datei zuerst angeklickt wurde.
        var uncertain = a.CreatedAt is null || b.CreatedAt is null || a.CreatedAt == b.CreatedAt;
        var (before, after) = !uncertain && a.CreatedAt > b.CreatedAt ? (b, a) : (a, b);

        ct.ThrowIfCancellationRequested();

        var match = MatchSystems(before.System, after.System);

        return new BackupComparison
        {
            Before = before,
            After = after,
            OrderUncertain = uncertain,
            SystemMatch = match,
            SystemMatchText = DescribeMatch(match, before.System, after.System),
            Metrics = BuildMetrics(before, after),
            Instances = CompareInstances(before, after),
            Scripts = CompareScripts(before, after, ct),
            Namespaces = CompareObjects(before, after, ct),
            Views = CompareViews(before, after, ct)
        };
    }

    // -------------------------------------------------------------- Herkunft

    /// <summary>
    /// Prüft, ob beide Backups aus derselben ioBroker-Installation stammen.
    ///
    /// Belastbar ist nur die Installations-UUID aus <c>system.meta.uuid</c> — sie wird bei
    /// der Erstinstallation vergeben und ändert sich danach nicht mehr. Fehlt sie (Skript-
    /// Backup, sehr altes Backitup), wird auf Hostname und IP-Adresse zurückgefallen; beide
    /// können sich im Alltag ändern, weshalb das Ergebnis dann nur „wahrscheinlich" lautet.
    ///
    /// Ein Unterschied ist kein Fehler: Zwei Systeme zu vergleichen ist ein legitimer Fall
    /// (Umzug auf neue Hardware, Abgleich mit einer Zweitinstallation). Das Ergebnis ist
    /// deshalb eine Einstufung, keine Sperre — die Entscheidung trifft die Oberfläche.
    /// </summary>
    public static SystemMatch MatchSystems(SystemIdentity a, SystemIdentity b)
    {
        if (a.InstallationId.Length > 0 && b.InstallationId.Length > 0)
            return string.Equals(a.InstallationId, b.InstallationId, StringComparison.OrdinalIgnoreCase)
                ? SystemMatch.Same
                : SystemMatch.Different;

        if (!a.IsKnown || !b.IsKnown) return SystemMatch.Unknown;

        // Ohne UUID entscheiden Hostname und Adresse. Widerspricht eines der beiden
        // Merkmale, gilt das als verschiedene Systeme.
        var hostKnown = a.Hostname.Length > 0 && b.Hostname.Length > 0;
        var addrKnown = a.Address.Length > 0 && b.Address.Length > 0;

        if (hostKnown && !string.Equals(a.Hostname, b.Hostname, StringComparison.OrdinalIgnoreCase))
            return SystemMatch.Different;
        if (addrKnown && a.Address != b.Address)
            return SystemMatch.Different;

        return hostKnown || addrKnown ? SystemMatch.Probable : SystemMatch.Unknown;
    }

    private static string DescribeMatch(SystemMatch match, SystemIdentity a, SystemIdentity b) => match switch
    {
        SystemMatch.Same =>
            $"Beide Backups stammen vom selben System:  {b.Describe()}",
        SystemMatch.Probable =>
            $"Vermutlich dasselbe System (keine Installations-ID im Backup):  {b.Describe()}",
        SystemMatch.Different =>
            $"Verschiedene Systeme:  vorher {a.Describe()}   —   nachher {b.Describe()}",
        _ =>
            "Herkunft nicht prüfbar — mindestens eines der Backups enthält keine Systemkennung."
    };

    // ------------------------------------------------------------- Kennzahlen

    private static List<MetricRow> BuildMetrics(BackupData before, BackupData after) => new()
    {
        new MetricRow { Label = "Objekte", Before = before.Objects.Count, After = after.Objects.Count },
        new MetricRow { Label = "States", Before = before.StateCount, After = after.StateCount },
        new MetricRow { Label = "Adapter-Instanzen", Before = before.Instances.Count, After = after.Instances.Count },
        new MetricRow { Label = "Skripte", Before = before.Scripts.Count, After = after.Scripts.Count },
        new MetricRow { Label = "davon aktiv", Before = before.ScriptsEnabled, After = after.ScriptsEnabled },
        new MetricRow { Label = "Aliasse", Before = before.AliasCount, After = after.AliasCount },
        new MetricRow { Label = "User-Datenpunkte", Before = before.UserDataCount, After = after.UserDataCount },
        new MetricRow { Label = "Enums", Before = before.EnumCount, After = after.EnumCount }
    };

    // --------------------------------------------------------------- Instanzen

    private static List<InstanceChange> CompareInstances(BackupData before, BackupData after)
    {
        // Instanz-Namespaces sind per Konstruktion kleingeschrieben; der Abgleich läuft
        // trotzdem case-insensitiv, weil hier keine Tippfehler-Dubletten zu erwarten sind.
        var b = before.Instances.ToDictionary(i => i.Namespace, StringComparer.OrdinalIgnoreCase);
        var a = after.Instances.ToDictionary(i => i.Namespace, StringComparer.OrdinalIgnoreCase);

        var result = new List<InstanceChange>();

        foreach (var ns in b.Keys.Union(a.Keys, StringComparer.OrdinalIgnoreCase))
        {
            var hasBefore = b.TryGetValue(ns, out var oldInst);
            var hasAfter = a.TryGetValue(ns, out var newInst);

            var kind = !hasBefore ? ChangeKind.Added
                     : !hasAfter ? ChangeKind.Removed
                     : oldInst!.Version != newInst!.Version
                       || oldInst.Enabled != newInst.Enabled
                       || oldInst.ObjectCount != newInst.ObjectCount ? ChangeKind.Changed
                     : ChangeKind.Unchanged;

            result.Add(new InstanceChange
            {
                Namespace = ns,
                Adapter = (newInst ?? oldInst)!.Adapter,
                Kind = kind,
                VersionBefore = oldInst?.Version ?? "",
                VersionAfter = newInst?.Version ?? "",
                EnabledBefore = oldInst?.Enabled,
                EnabledAfter = newInst?.Enabled,
                ObjectsBefore = oldInst?.ObjectCount ?? 0,
                ObjectsAfter = newInst?.ObjectCount ?? 0
            });
        }

        // Änderungen zuerst, darin alphabetisch — die unveränderten Instanzen sind Beiwerk.
        return result
            .OrderBy(i => i.Kind == ChangeKind.Unchanged)
            .ThenBy(i => i.Namespace, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // ----------------------------------------------------------------- Skripte

    private static List<ScriptChange> CompareScripts(BackupData before, BackupData after,
                                                     CancellationToken ct)
    {
        var b = new Dictionary<string, ScriptInfo>(StringComparer.Ordinal);
        foreach (var s in before.Scripts) b[s.Id] = s;
        var a = new Dictionary<string, ScriptInfo>(StringComparer.Ordinal);
        foreach (var s in after.Scripts) a[s.Id] = s;

        var result = new List<ScriptChange>();

        foreach (var id in b.Keys.Union(a.Keys, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();

            var hasBefore = b.TryGetValue(id, out var oldScript);
            var hasAfter = a.TryGetValue(id, out var newScript);

            if (!hasBefore)
            {
                result.Add(new ScriptChange
                {
                    Id = id,
                    DisplayPath = newScript!.DisplayPath,
                    Kind = ChangeKind.Added,
                    After = newScript,
                    AddedLines = CountLines(ScriptChange.ComparableText(newScript))
                });
                continue;
            }

            if (!hasAfter)
            {
                result.Add(new ScriptChange
                {
                    Id = id,
                    DisplayPath = oldScript!.DisplayPath,
                    Kind = ChangeKind.Removed,
                    Before = oldScript,
                    RemovedLines = CountLines(ScriptChange.ComparableText(oldScript))
                });
                continue;
            }

            var oldText = ScriptChange.ComparableText(oldScript!);
            var newText = ScriptChange.ComparableText(newScript!);

            var contentChanged = !string.Equals(oldText, newText, StringComparison.Ordinal);
            var statusChanged = oldScript!.Enabled != newScript!.Enabled;

            var added = 0;
            var removed = 0;
            if (contentChanged) (added, removed) = TextDiff.CountChanges(oldText, newText);

            result.Add(new ScriptChange
            {
                Id = id,
                DisplayPath = newScript.DisplayPath,
                Kind = contentChanged || statusChanged ? ChangeKind.Changed : ChangeKind.Unchanged,
                Before = oldScript,
                After = newScript,
                AddedLines = added,
                RemovedLines = removed
            });
        }

        return result
            .OrderBy(s => s.Kind == ChangeKind.Unchanged)
            .ThenBy(s => s.DisplayPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int CountLines(string text) =>
        string.IsNullOrEmpty(text) ? 0 : text.Replace("\r\n", "\n").Split('\n').Length;

    // ----------------------------------------------------------------- Objekte

    private static List<NamespaceChange> CompareObjects(BackupData before, BackupData after,
                                                        CancellationToken ct)
    {
        var b = new HashSet<string>(before.Objects.Select(o => o.Id), StringComparer.Ordinal);
        var a = new HashSet<string>(after.Objects.Select(o => o.Id), StringComparer.Ordinal);

        var byNamespace = new Dictionary<string, NamespaceChange>(StringComparer.Ordinal);

        NamespaceChange Bucket(string id)
        {
            var ns = NamespaceOf(id);
            if (!byNamespace.TryGetValue(ns, out var n))
            {
                n = new NamespaceChange { Namespace = ns };
                byNamespace[ns] = n;
            }
            return n;
        }

        foreach (var id in a)
        {
            ct.ThrowIfCancellationRequested();
            if (!b.Contains(id)) Bucket(id).AddedIds.Add(id);
        }

        foreach (var id in b)
        {
            ct.ThrowIfCancellationRequested();
            if (!a.Contains(id)) Bucket(id).RemovedIds.Add(id);
        }

        foreach (var n in byNamespace.Values)
        {
            n.AddedIds.Sort(StringComparer.OrdinalIgnoreCase);
            n.RemovedIds.Sort(StringComparer.OrdinalIgnoreCase);
        }

        // Größte Bewegung zuerst — dort steckt in aller Regel die Ursache.
        return byNamespace.Values
            .OrderByDescending(n => n.Added + n.Removed)
            .ThenBy(n => n.Namespace, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Namensraum einer ID: die ersten beiden Segmente (adapter.instanz), bei
    /// System-IDs ohne Instanznummer das erste Segment.
    /// </summary>
    private static string NamespaceOf(string id)
    {
        var first = id.IndexOf('.');
        if (first < 0) return id;

        var second = id.IndexOf('.', first + 1);
        if (second < 0) return id;

        var instancePart = id[(first + 1)..second];
        return int.TryParse(instancePart, out _) ? id[..second] : id[..first];
    }

    // -------------------------------------------------------------- VIS-Views

    private static List<ViewChange> CompareViews(BackupData before, BackupData after,
                                                 CancellationToken ct)
    {
        var result = new List<ViewChange>();

        foreach (var version in new[] { VisVersion.Vis1, VisVersion.Vis2 })
        {
            ct.ThrowIfCancellationRequested();

            var b = ReadViews(before, version);
            var a = ReadViews(after, version);

            // Kommt eine VIS-Version in nur einem der Backups vor, wäre jede View
            // „neu" oder „entfernt" — das ist keine Aussage über die Views, sondern
            // über das Backup. Solche Fälle werden übersprungen.
            if (b.Count == 0 || a.Count == 0) continue;

            foreach (var view in b.Keys.Union(a.Keys, StringComparer.Ordinal))
            {
                var hasBefore = b.TryGetValue(view, out var oldWidgets);
                var hasAfter = a.TryGetValue(view, out var newWidgets);

                if (!hasBefore)
                {
                    result.Add(new ViewChange
                    {
                        Version = version, View = view, Kind = ChangeKind.Added,
                        WidgetsAfter = newWidgets!.Count, WidgetsAdded = newWidgets.Count
                    });
                    continue;
                }

                if (!hasAfter)
                {
                    result.Add(new ViewChange
                    {
                        Version = version, View = view, Kind = ChangeKind.Removed,
                        WidgetsBefore = oldWidgets!.Count, WidgetsRemoved = oldWidgets.Count
                    });
                    continue;
                }

                var addedW = newWidgets!.Keys.Count(k => !oldWidgets!.ContainsKey(k));
                var removedW = oldWidgets!.Keys.Count(k => !newWidgets.ContainsKey(k));
                var changedW = oldWidgets.Count(kv =>
                    newWidgets.TryGetValue(kv.Key, out var other) && other != kv.Value);

                result.Add(new ViewChange
                {
                    Version = version,
                    View = view,
                    Kind = addedW + removedW + changedW > 0 ? ChangeKind.Changed : ChangeKind.Unchanged,
                    WidgetsBefore = oldWidgets.Count,
                    WidgetsAfter = newWidgets.Count,
                    WidgetsAdded = addedW,
                    WidgetsRemoved = removedW,
                    WidgetsChanged = changedW
                });
            }
        }

        return result
            .OrderBy(v => v.Kind == ChangeKind.Unchanged)
            .ThenBy(v => v.Version)
            .ThenBy(v => v.View, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Liest die Views einer VIS-Version als Zuordnung View → (Widget-ID → Inhaltssignatur).
    /// Als Signatur dient der Roh-JSON-Text des Widgets: jede Änderung an Position, Stil
    /// oder Datenpunkt verändert ihn, ohne dass das Widget-Format bekannt sein muss.
    /// </summary>
    private static Dictionary<string, Dictionary<string, string>> ReadViews(BackupData data,
                                                                            VisVersion version)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        foreach (var file in data.VisViews.Where(v => v.Version == version))
        {
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(file.Content);
            }
            catch (JsonException)
            {
                continue;   // unlesbare View-Datei darf den Vergleich nicht sprengen
            }

            using (doc)
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Object) continue;

                foreach (var view in doc.RootElement.EnumerateObject())
                {
                    // ___settings enthält globale Einstellungen, keine View.
                    if (view.Name.StartsWith("___", StringComparison.Ordinal)) continue;
                    if (view.Value.ValueKind != JsonValueKind.Object) continue;

                    var widgets = new Dictionary<string, string>(StringComparer.Ordinal);

                    if (view.Value.TryGetProperty("widgets", out var w) && w.ValueKind == JsonValueKind.Object)
                        foreach (var widget in w.EnumerateObject())
                            widgets[widget.Name] = widget.Value.GetRawText();

                    result[view.Name] = widgets;
                }
            }
        }

        return result;
    }
}

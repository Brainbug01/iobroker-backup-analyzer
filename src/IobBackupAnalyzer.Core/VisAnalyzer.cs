using System.Text.Json;
using System.Text.RegularExpressions;

namespace IobBackupAnalyzer.Core;

/// <summary>
/// Liest aus den vis-views.json die tatsächlich verwendeten Datenpunkte aus — getrennt
/// nach VIS 1 (vis.0) und VIS 2 (vis-2.0).
///
/// Struktur (an einem echten Backup verifiziert, beide Versionen identisch aufgebaut):
///   { "&lt;ViewName&gt;": { "widgets": { "&lt;WidgetId&gt;": { "data": { … } } } }, "___settings": {…} }
///
/// Datenpunkte stecken im data-Objekt und werden auf zwei Wegen gefunden:
///   1. Jeder Schlüssel, der "oid" enthält — oid, visibility-oid, signals-oid-0,
///      lc-oid, countdown_oid, iTblCellThresholds-oid3, oid1…oidN.
///   2. VIS-Bindings in beliebigen Textwerten: {id}, {id;format}, {a:id1;b:id2;a+b}.
/// </summary>
public static class VisAnalyzer
{
    /// <summary>VIS trägt bei nicht belegten Feldern diesen Platzhalter ein.</summary>
    private const string Placeholder = "nothing_selected";

    /// <summary>
    /// Zustandsattribute, die ein VIS-Binding an eine Objekt-ID anhängen kann:
    /// {0_userdata.0.Beispiel.Wert.ts} liest den Zeitstempel, nicht einen Datenpunkt namens „ts".
    /// Ohne Suffix gilt der Wert selbst (val).
    /// </summary>
    private static readonly string[] StateAttributes =
    {
        "val", "ts", "lc", "ack", "q", "from", "user", "expire"
    };

    /// <summary>Inhalt eines VIS-Bindings zwischen geschweiften Klammern.</summary>
    private static readonly Regex Binding =
        new(@"\{([^{}]+)\}", RegexOptions.Compiled, RegexLimits.MatchTimeout);

    /// <summary>
    /// Eine ioBroker-Objekt-ID: mindestens zwei durch Punkt getrennte Segmente, keine
    /// Leerzeichen. Filtert CSS-Fragmente und Freitext aus den Bindings heraus.
    /// </summary>
    private static readonly Regex LooksLikeId =
        new(@"^[A-Za-z0-9_][A-Za-z0-9_\-]*(\.[A-Za-z0-9_\-][^.\s]*)+$", RegexOptions.Compiled,
            RegexLimits.MatchTimeout);

    public static List<VisDatapoint> Analyze(BackupData data, CancellationToken ct = default)
    {
        // ioBroker-IDs sind case-sensitiv: 0_userdata.0.X.Wert und …wert sind
        // zwei verschiedene Objekte. Deshalb durchgaengig StringComparer.Ordinal.
        var byId = new Dictionary<string, VisDatapoint>(StringComparer.Ordinal);

        foreach (var file in data.VisViews)
        {
            ct.ThrowIfCancellationRequested();
            CollectFromFile(file, byId, ct);
        }

        // Gegen den Objektbestand abgleichen: Was hier fehlt, ist ein totes Widget.
        // Kein ToDictionary — bei gleichnamigen IDs (theoretisch moeglich) wuerde es werfen.
        var known = new Dictionary<string, IobObject>(StringComparer.Ordinal);
        foreach (var o in data.Objects) known[o.Id] = o;

        ResolveStateAttributes(byId, known);

        foreach (var dp in byId.Values)
        {
            if (!known.TryGetValue(dp.Id, out var obj)) continue;

            dp.ExistsInBackup = true;
            dp.Name = obj.Name;

            // Bei einem Alias interessiert, worauf er zeigt — und ob dieses Ziel noch da ist.
            if (!string.IsNullOrEmpty(obj.AliasTarget))
            {
                dp.AliasTarget = obj.AliasTarget;
                dp.AliasTargetMissing = !known.ContainsKey(obj.AliasTarget);
            }
        }

        return byId.Values.OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Führt Attributzugriffe auf ihren Datenpunkt zurück.
    ///
    /// Ein Binding wie {0_userdata.0.Meldungen.Text.ts;date(hh:mm)} liest den
    /// Zeitstempel des Datenpunkts …Meldungen.Text — es gibt keinen Datenpunkt „…Text.ts".
    /// Ohne diese Auflösung erschiene der Zugriff als totes Widget.
    ///
    /// Korrigiert wird nur, wenn die ID selbst nicht existiert, das letzte Segment ein
    /// bekanntes Zustandsattribut ist und der Datenpunkt ohne dieses Segment existiert.
    /// Damit bleiben echte Datenpunkte unangetastet, die zufällig auf .ts oder .from enden.
    /// </summary>
    private static void ResolveStateAttributes(Dictionary<string, VisDatapoint> byId,
                                               Dictionary<string, IobObject> known)
    {
        var toRemove = new List<string>();

        foreach (var (id, dp) in byId.ToList())
        {
            if (known.ContainsKey(id)) continue;

            var lastDot = id.LastIndexOf('.');
            if (lastDot <= 0) continue;

            var suffix = id[(lastDot + 1)..];
            if (!StateAttributes.Contains(suffix, StringComparer.Ordinal)) continue;

            var baseId = id[..lastDot];
            if (!known.ContainsKey(baseId)) continue;

            // Attribut an allen Fundstellen vermerken …
            foreach (var usage in dp.Usages) usage.Attribute = suffix;

            // … und die Fundstellen dem echten Datenpunkt zuschlagen.
            if (!byId.TryGetValue(baseId, out var target))
            {
                target = new VisDatapoint { Id = baseId };
                byId[baseId] = target;
            }

            foreach (var usage in dp.Usages)
            {
                target.Usages.Add(usage);
                if (usage.Version == VisVersion.Vis1) target.Vis1Views.Add(usage.ViewPath);
                else target.Vis2Views.Add(usage.ViewPath);
            }

            toRemove.Add(id);
        }

        foreach (var id in toRemove) byId.Remove(id);
    }

    private static void CollectFromFile(VisFile file, Dictionary<string, VisDatapoint> byId,
                                        CancellationToken ct)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(file.Content);
        }
        catch (JsonException)
        {
            return;   // unlesbare View-Datei darf den Rest nicht verhindern
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return;

            foreach (var view in doc.RootElement.EnumerateObject())
            {
                ct.ThrowIfCancellationRequested();

                // ___settings enthält globale Einstellungen, keine Views.
                if (view.Name.StartsWith("___", StringComparison.Ordinal)) continue;
                if (view.Value.ValueKind != JsonValueKind.Object) continue;

                if (!view.Value.TryGetProperty("widgets", out var widgets)) continue;
                if (widgets.ValueKind != JsonValueKind.Object) continue;

                foreach (var widget in widgets.EnumerateObject())
                {
                    if (widget.Value.ValueKind != JsonValueKind.Object) continue;
                    if (!widget.Value.TryGetProperty("data", out var wdata)) continue;
                    if (wdata.ValueKind != JsonValueKind.Object) continue;

                    var tpl = ReadString(widget.Value, "tpl");
                    var widgetSet = ReadString(widget.Value, "widgetSet");
                    var widgetName = ReadString(wdata, "name");

                    foreach (var field in wdata.EnumerateObject())
                    {
                        if (field.Value.ValueKind != JsonValueKind.String) continue;
                        var value = field.Value.GetString();
                        if (string.IsNullOrWhiteSpace(value)) continue;

                        // 1. Schlüssel mit "oid" — der Wert ist direkt eine Datenpunkt-ID.
                        if (field.Name.Contains("oid", StringComparison.OrdinalIgnoreCase))
                            Add(byId, value, new VisUsage
                            {
                                Version = file.Version,
                                Project = file.Project,
                                View = view.Name,
                                WidgetId = widget.Name,
                                Template = tpl,
                                WidgetSet = widgetSet,
                                WidgetName = widgetName,
                                Field = field.Name
                            });

                        // 2. Bindings in beliebigen Textfeldern.
                        if (value.Contains('{'))
                            foreach (var id in ExtractBindingIds(value))
                                Add(byId, id, new VisUsage
                                {
                                    Version = file.Version,
                                    Project = file.Project,
                                    View = view.Name,
                                    WidgetId = widget.Name,
                                    Template = tpl,
                                    WidgetSet = widgetSet,
                                    WidgetName = widgetName,
                                    Field = field.Name + " (Binding)"
                                });
                    }
                }
            }
        }
    }

    /// <summary>
    /// Zerlegt ein VIS-Binding und gibt die enthaltenen Objekt-IDs zurück.
    /// Beispiele: {id}, {id;date(hh:mm)}, {a:id1;b:id2;a+b}
    /// </summary>
    private static IEnumerable<string> ExtractBindingIds(string text)
    {
        List<Match> matches;
        try
        {
            // Das ToList ist nicht kosmetisch: MatchCollection wertet verzögert aus, ohne
            // es liefe die Suche erst in der foreach-Schleife — außerhalb des try.
            matches = Binding.Matches(text).ToList();
        }
        catch (RegexMatchTimeoutException)
        {
            yield break;   // ein zäher Textwert kostet seine Bindings, nicht die Analyse
        }

        foreach (var m in matches)
        {
            var inner = m.Groups[1].Value;

            // Semikolon trennt ID von Formatierung bzw. mehrere Operanden voneinander.
            foreach (var part in inner.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = part.Trim();

                // "a:0_userdata.0.x" — der Doppelpunkt bindet einen Variablennamen an die ID.
                var colon = candidate.IndexOf(':');
                if (colon > 0 && colon < candidate.Length - 1)
                    candidate = candidate[(colon + 1)..].Trim();

                if (IsId(candidate)) yield return candidate;
            }
        }
    }

    /// <summary>
    /// Der ID-Filter über einem Wert aus dem Backup. Läuft er in die Zeitgrenze, gilt der
    /// Wert als keine ID — der Filter ist ohnehin dazu da, Freitext auszusortieren.
    /// </summary>
    private static bool IsId(string candidate)
    {
        try
        {
            return LooksLikeId.IsMatch(candidate);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static string ReadString(JsonElement obj, string property) =>
        obj.TryGetProperty(property, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString() ?? "" : "";

    private static void Add(Dictionary<string, VisDatapoint> byId, string id, VisUsage usage)
    {
        id = id.Trim();
        if (id.Length == 0) return;
        if (id.Equals(Placeholder, StringComparison.OrdinalIgnoreCase)) return;
        if (!IsId(id)) return;

        if (!byId.TryGetValue(id, out var dp))
        {
            dp = new VisDatapoint { Id = id };
            byId[id] = dp;
        }

        dp.Usages.Add(usage);
        if (usage.Version == VisVersion.Vis1) dp.Vis1Views.Add(usage.ViewPath);
        else dp.Vis2Views.Add(usage.ViewPath);
    }
}

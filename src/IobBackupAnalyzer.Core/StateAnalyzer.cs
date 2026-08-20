namespace IobBackupAnalyzer.Core;

/// <summary>
/// Wertet states.jsonl aus — die einzige Stelle im Backup, die etwas über die
/// tatsächliche Nutzung eines Datenpunkts aussagt statt nur über seine Existenz.
///
/// Vier Blickwinkel:
///   • States ohne Objekt — Werte-Leichen in der States-Datenbank. Die Objekt-Leichen-Analyse
///     (Säule 3, Analyse A) findet sie prinzipbedingt nicht, weil sie nur Objekte kennt.
///   • Objekte ohne State — angelegt, aber nie beschrieben.
///   • Alter der letzten Wertänderung — trennt lebende von eingeschlafenen Datenpunkten.
///   • Qualitätscode und fehlende Quittierung — laufende Störungen zum Backup-Zeitpunkt.
///
/// Der Wert selbst wird nie geladen (siehe <see cref="StateInfo"/>).
/// </summary>
public static class StateAnalyzer
{
    /// <summary>
    /// Verwaltungs-States, die kein Datenpunkt im eigentlichen Sinn sind. Sie liegen
    /// systembedingt außerhalb des Objektbestands und würden die Liste der Werte-Leichen
    /// unbrauchbar auffüllen.
    /// </summary>
    private static bool IsInfrastructure(string id) =>
        id.StartsWith("system.adapter.", StringComparison.Ordinal)
        || id.StartsWith("system.host.", StringComparison.Ordinal);

    public static StateReport Analyze(BackupData data, CancellationToken ct = default)
    {
        var reference = data.CreatedAt ?? DateTime.Now;

        // Objektbestand als Nachschlagewerk. Ordinal, weil ioBroker-IDs case-sensitiv sind.
        var objects = new Dictionary<string, IobObject>(StringComparer.Ordinal);
        foreach (var o in data.Objects) objects[o.Id] = o;

        var all = new List<StateRow>(data.States.Count);
        var withoutObject = new List<StateRow>();
        var badQuality = new List<StateRow>();
        var unacked = new List<StateRow>();

        foreach (var st in data.States.Values)
        {
            ct.ThrowIfCancellationRequested();

            var hasObject = objects.TryGetValue(st.Id, out var obj);
            var row = ToRow(st, obj, hasObject, reference);

            all.Add(row);

            if (!hasObject && !IsInfrastructure(st.Id)) withoutObject.Add(row);
            if (st.Quality != 0) badQuality.Add(row);
            if (!st.Ack) unacked.Add(row);
        }

        // Gegenrichtung: Objekte vom Typ state, zu denen kein Wert existiert.
        var withoutState = new List<StateRow>();
        var stateObjects = 0;
        var aliases = 0;

        foreach (var o in data.Objects)
        {
            ct.ThrowIfCancellationRequested();
            if (o.Type != "state") continue;

            stateObjects++;
            if (data.States.ContainsKey(o.Id)) continue;

            // Ein Alias hat systembedingt keinen eigenen Eintrag in der States-DB: Der
            // js-controller reicht Lesen und Schreiben an das Ziel aus common.alias.id
            // durch. Das betrifft ausnahmslos jeden Alias — sie standen bis v1.14.0
            // ausnahmslos in dieser Liste, obwohl jeder einzelne einwandfrei arbeitet
            // (Rückfrage aus der Praxis: ein Alias auf einen Shelly, dessen Ziel am Vortag
            // geschrieben wurde). Ob ein Alias ein gültiges Ziel hat, prüft der Tab
            // „Aliasse"; hier wäre er nur ein Fehlalarm.
            if (IsAlias(o)) { aliases++; continue; }

            withoutState.Add(new StateRow
            {
                Id = o.Id,
                Name = o.Name,
                ObjectType = o.Type,
                HasObject = true,
                HasState = false,
                QualityText = "",
                Writable = o.Writable
            });
        }

        return new StateReport
        {
            All = all.OrderByDescending(r => r.AgeDays ?? int.MaxValue)
                     .ThenBy(r => r.Id, StringComparer.OrdinalIgnoreCase).ToList(),
            StatesWithoutObject = Sorted(withoutObject),
            ObjectsWithoutState = withoutState.OrderBy(r => r.Id, StringComparer.OrdinalIgnoreCase).ToList(),
            BadQuality = Sorted(badQuality),
            Unacknowledged = Sorted(unacked),
            Ages = BuildBuckets(all),
            TotalStates = data.States.Count,
            TotalStateObjects = stateObjects,
            AliasesWithoutOwnState = aliases
        };
    }

    /// <summary>
    /// true, wenn das Objekt ein Alias ist. Maßgeblich ist der Namensraum <c>alias.</c> —
    /// dort liegen sie ausnahmslos, und ein Alias-Objekt ohne <c>common.alias.id</c> ist
    /// kein nie beschriebener Datenpunkt, sondern ein defekter Alias (Tab „Aliasse").
    /// </summary>
    private static bool IsAlias(IobObject o) =>
        o.Id.StartsWith("alias.", StringComparison.OrdinalIgnoreCase) || o.AliasTarget is not null;

    /// <summary>Älteste zuerst — das ist in allen vier Listen die interessante Richtung.</summary>
    private static List<StateRow> Sorted(List<StateRow> rows) =>
        rows.OrderByDescending(r => r.AgeDays ?? int.MaxValue)
            .ThenBy(r => r.Id, StringComparer.OrdinalIgnoreCase).ToList();

    private static StateRow ToRow(StateInfo st, IobObject? obj, bool hasObject, DateTime reference)
    {
        var last = st.LastChange;
        return new StateRow
        {
            Id = st.Id,
            Name = obj?.Name ?? "",
            ObjectType = obj?.Type ?? "",
            HasObject = hasObject,
            HasState = true,
            LastChange = last,
            AgeDays = last is null ? null : (int)(reference - last.Value).TotalDays,
            From = st.FromShort,
            Quality = st.Quality,
            QualityText = st.QualityText,
            Ack = st.Ack
        };
    }

    /// <summary>
    /// Altersverteilung über alle States. Die Grenzen sind bewusst grob — es geht um die
    /// Frage „lebt das noch?", nicht um exakte Statistik.
    /// </summary>
    private static List<AgeBucket> BuildBuckets(List<StateRow> rows)
    {
        var buckets = new List<AgeBucket>
        {
            new() { Label = "letzte 24 Stunden", MinDays = 0 },
            new() { Label = "letzte 7 Tage",     MinDays = 1 },
            new() { Label = "letzte 30 Tage",    MinDays = 7 },
            new() { Label = "letzte 90 Tage",    MinDays = 30 },
            new() { Label = "letztes Jahr",      MinDays = 90 },
            new() { Label = "älter als 1 Jahr",  MinDays = 365 },
            new() { Label = "ohne Zeitstempel",  MinDays = int.MaxValue }
        };

        foreach (var r in rows)
        {
            if (r.AgeDays is not { } age) { buckets[^1].Count++; continue; }

            var idx = age switch
            {
                < 1 => 0,
                < 7 => 1,
                < 30 => 2,
                < 90 => 3,
                < 365 => 4,
                _ => 5
            };
            buckets[idx].Count++;
        }

        return buckets;
    }
}

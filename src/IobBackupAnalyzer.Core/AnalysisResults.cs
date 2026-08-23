namespace IobBackupAnalyzer.Core;

/// <summary>
/// Die vorberechneten Ergebnisse der aufwendigen Analysen.
///
/// <b>Warum es das gibt.</b> Die drei schweren Auswertungen — Verwendung, Waisen und VIS —
/// liefen bisher dort, wo der jeweilige Tab seine Daten bekam, und das ist der UI-Thread.
/// Bei einer kleinen Anlage fällt das nicht auf; bei einer großen friert das Fenster für die
/// gesamte Rechenzeit ein, und Windows beschriftet es mit „Keine Rückmeldung". Von außen
/// sieht das aus wie ein Absturz.
///
/// Gerechnet wird deshalb einmal im Hintergrund, direkt nach dem Laden. Die Tabs bekommen
/// das fertige Ergebnis und füllen nur noch ihre Listen.
///
/// <b>Warum trotzdem alles auf einmal?</b> Nicht jeder Tab wird angesehen, und man könnte
/// erst rechnen, wenn jemand hinschaut. Dann wandert die Wartezeit aber genau dorthin, wo
/// sie am meisten stört: mitten in die Bedienung. Ein Ladevorgang, bei dem sichtbar etwas
/// passiert, ist die bessere Stelle dafür.
/// </summary>
public sealed class AnalysisResults
{
    /// <summary>Kreuzreferenz Skript ↔ Datenpunkt.</summary>
    public UsageReport? Usage { get; init; }

    /// <summary>Analyse A — Objekte ohne zugehörige Adapter-Instanz.</summary>
    public List<OrphanObject>? Orphans { get; init; }

    /// <summary>Analyse B — eigene Datenpunkte, die nirgends verwendet zu werden scheinen.</summary>
    public List<UnusedDatapoint>? Unused { get; init; }

    /// <summary>Analyse C — Auswertung der states.jsonl.</summary>
    public StateReport? States { get; init; }

    /// <summary>Die in VIS verwendeten Datenpunkte.</summary>
    public List<VisDatapoint>? Vis { get; init; }

    /// <summary>
    /// Rechnet alles durch — gedacht für einen Hintergrund-Thread. Bei einem Skript-Backup
    /// gibt es keinen Objektbestand; dann bleibt alles leer, statt Aussagen über einen
    /// Bestand zu treffen, den es nicht gibt.
    /// </summary>
    /// <param name="progress">
    /// Meldet den gerade laufenden Schritt an die Oberfläche. Die Analysen sind seit dem
    /// Index schnell, aber „schnell" hängt an der Anlage — und eine Statuszeile, die den
    /// Schritt benennt, ist auch dann richtig, wenn er nur einen Wimpernschlag dauert.
    /// </param>
    public static AnalysisResults Compute(BackupData data, LoadLog? log = null,
                                          IProgress<string>? progress = null,
                                          CancellationToken ct = default)
    {
        if (data.Kind != BackupKind.Full) return new AnalysisResults();

        void Schritt(int nummer, string name)
        {
            log?.Step($"Analyse: {name}");
            progress?.Report($"Analyse {nummer}/5: {name} …");
        }

        Schritt(1, "Verwendung");
        var usage = UsageAnalyzer.Analyze(data, ct);
        ct.ThrowIfCancellationRequested();

        log?.Step($"Kreuzreferenz: {usage.States.Count:N0} Datenpunkte");
        Schritt(2, "verwaiste Objekte (A)");
        var orphans = OrphanAnalyzer.FindOrphanObjects(data);
        ct.ThrowIfCancellationRequested();

        Schritt(3, "unbenutzte Datenpunkte (B)");
        var unused = OrphanAnalyzer.FindUnusedDatapoints(data);
        ct.ThrowIfCancellationRequested();

        Schritt(4, "States (C)");
        var states = StateAnalyzer.Analyze(data, ct);
        ct.ThrowIfCancellationRequested();

        Schritt(5, "VIS");
        var vis = VisAnalyzer.Analyze(data);
        ct.ThrowIfCancellationRequested();

        log?.Step("Analysen fertig");

        return new AnalysisResults
        {
            Usage = usage,
            Orphans = orphans,
            Unused = unused,
            States = states,
            Vis = vis
        };
    }
}

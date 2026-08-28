namespace IobBackupAnalyzer.Core;

/// <summary>
/// Ein Befund: Diese Backitup-Instanz soll die Verlaufsdaten sichern, hat dafür aber keinen
/// Pfad hinterlegt.
/// </summary>
/// <param name="BackitupInstance">Die betroffene Instanz, z. B. <c>backitup.0</c>.</param>
/// <param name="HistoryInstances">
/// Die laufenden History-Instanzen, deren Daten davon betroffen sind — mindestens eine,
/// sonst gäbe es den Befund nicht.
/// </param>
public sealed record HistoryBackupFinding(string BackitupInstance,
                                          IReadOnlyList<string> HistoryInstances);

/// <summary>
/// Eine einzige Prüfung: <b>Die History-Sicherung ist eingeschaltet, aber ohne Pfad.</b>
///
/// <b>Warum das ein Befund ist.</b> Der history-Adapter schreibt seine Verläufe in Dateien
/// unter <c>iobroker-data</c>. Anders als etwa der zigbee-Adapter meldet er dafür kein
/// <c>common.dataFolder</c> an — und genau daran entscheidet der js-controller, welche
/// Ordner in ein <c>iobroker backup</c> wandern. Verlaufsdaten sind deshalb in einem
/// gewöhnlichen Voll-Backup <b>nie</b> enthalten. Wer sie gesichert haben will, braucht die
/// eigene History-Sicherung von Backitup — und die packt den Ordner, der in ihrer
/// Einstellung steht. Steht dort nichts, entsteht trotzdem ein Archiv, es enthält nur die
/// Verläufe nicht. Gemeldet wird das nirgends: Ein Fehlschlag dieser Teilsicherung bricht
/// den Lauf nicht ab, das Backup gilt als erfolgreich.
///
/// <b>Und warum nur dieser eine Fall.</b> Es wäre ein Leichtes, hier weitere Unstimmigkeiten
/// anzuzeigen — eine abgeschaltete History-Sicherung bei laufendem history-Adapter, einen
/// Pfad, der woandershin zeigt als der Adapter schreibt, dasselbe für InfluxDB und SQL. Das
/// ist bewusst unterlassen: Jeder dieser Fälle kann so gewollt sein, und eine Prüfung, die
/// bei jedem Backup etwas anzumerken hat, wird überlesen. Ein Haken für „History sichern"
/// bei leerem Pfad dagegen ist immer ein Versehen. Nur dafür gibt es hier eine Zeile —
/// sonst schweigt die Prüfung, auch ohne bestätigende Meldung.
///
/// <b>Der Befund ist eine Warnung, kein Nachruf.</b> Die Aufzeichnung läuft unabhängig von
/// der Sicherung weiter; die Daten liegen auf dem Rechner. Wer den Pfad einträgt, hat sie ab
/// dem nächsten Backup vollständig im Archiv — auch rückwirkend, soweit die eingestellte
/// Aufbewahrungsdauer sie nicht längst gelöscht hat.
/// </summary>
public static class HistoryBackupAnalyzer
{
    /// <summary>
    /// Die Befunde, höchstens einer je Backitup-Instanz. Leere Liste heißt: nichts zu melden.
    ///
    /// Gemeldet wird nur, wenn <b>alle</b> Bedingungen zutreffen:
    /// <list type="bullet">
    ///   <item>Es läuft mindestens eine aktivierte <c>history</c>-Instanz — ohne sie gibt es
    ///         keine Verlaufsdaten, die verlorengehen könnten.</item>
    ///   <item>Es gibt eine aktivierte <c>backitup</c>-Instanz, deren Konfiguration im Backup
    ///         steht. Fehlt der native-Abschnitt, wissen wir nichts und behaupten nichts.</item>
    ///   <item>Dort ist die History-Sicherung eingeschaltet und der Pfad leer.</item>
    /// </list>
    ///
    /// Dass auch die Backitup-Instanz aktiviert sein muss, ist eine bewusste Entscheidung:
    /// Bei einer abgeschalteten Instanz sichert ohnehin nichts, und über eine stillgelegte
    /// Konfiguration eine Warnung auszugeben hieße, den Nutzer auf etwas zu stoßen, das ihn
    /// gerade nicht betrifft.
    /// </summary>
    public static List<HistoryBackupFinding> Analyze(BackupData data)
    {
        var befunde = new List<HistoryBackupFinding>();

        var history = data.Instances
            .Where(i => i.Enabled
                        && string.Equals(i.Adapter, "history", StringComparison.OrdinalIgnoreCase))
            .Select(i => i.Namespace)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (history.Count == 0) return befunde;

        foreach (var inst in data.Instances)
        {
            if (!inst.Enabled) continue;
            if (!string.Equals(inst.Adapter, "backitup", StringComparison.OrdinalIgnoreCase))
                continue;

            // Die Einstellung hängt am Instanz-Objekt, nicht an der aufbereiteten Instanz:
            // Sie stammt aus native, und dort schaut nur der Parser hin.
            var obj = data.Objects.FirstOrDefault(
                o => o.Type == "instance"
                     && string.Equals(o.Id, $"system.adapter.{inst.Namespace}",
                                      StringComparison.OrdinalIgnoreCase));

            var setting = obj?.HistoryBackup;
            if (setting is null) continue;              // keine Angabe im Backup
            if (!setting.Enabled) continue;             // bewusst abgeschaltet
            if (setting.PathSet) continue;              // Pfad vorhanden — nichts zu melden

            befunde.Add(new HistoryBackupFinding(inst.Namespace, history));
        }

        return befunde;
    }
}

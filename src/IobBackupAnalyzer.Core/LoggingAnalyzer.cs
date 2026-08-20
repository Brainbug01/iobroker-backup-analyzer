namespace IobBackupAnalyzer.Core;

/// <summary>
/// Logging-Übersicht: welche Datenpunkte werden von welcher Instanz geloggt und mit
/// welchen Optionen. Datengrundlage ist <c>common.custom</c>, das nach Instanz
/// geschlüsselt ist — history, influxdb, sql, sourceanalytix … in beliebiger
/// Instanznummer. Alle Instanzen werden ausgewertet, keine wird bevorzugt.
///
/// Ein deaktivierter Eintrag (<c>enabled=false</c>) bleibt in der Liste: Er zeigt eine
/// konfigurierte, aber abgeschaltete Logging-Verbindung — genau der Fall, den man beim
/// Aufräumen sucht (z. B. eine History, die nicht mehr genutzt wird).
/// </summary>
public static class LoggingAnalyzer
{
    public static List<LoggingRow> Analyze(BackupData data)
    {
        var rows = new List<LoggingRow>();

        foreach (var o in data.Objects)
        {
            if (o.CustomLogging is null) continue;

            foreach (var c in o.CustomLogging)
                rows.Add(new LoggingRow
                {
                    Id = o.Id,
                    Name = o.Name,
                    Instance = c.Instance,
                    Adapter = c.Adapter,
                    Enabled = c.Enabled,
                    ChangesOnly = c.ChangesOnly,
                    DebounceMs = c.DebounceMs,
                    AliasId = c.AliasId
                });
        }

        return rows
            .OrderBy(r => r.Instance, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

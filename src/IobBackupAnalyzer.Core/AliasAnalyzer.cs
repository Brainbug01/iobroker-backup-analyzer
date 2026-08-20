namespace IobBackupAnalyzer.Core;

/// <summary>
/// Alias-Übersicht: alle Alias-Datenpunkte (<c>alias.*</c>, type=state) mit ihrem Lese-
/// und Schreibziel und der Angabe, ob diese Ziele im Backup noch existieren. Ein Alias,
/// dessen Ziel fehlt, führt ins Leere — das deckt diese Auswertung auf.
///
/// <c>common.alias.id</c> ist entweder ein String (Lesen = Schreiben) oder ein Objekt
/// { read, write }; beide Formen werden im Parser getrennt erfasst. Ziel-IDs enthalten
/// die Instanznummer der Quelle (etwa .1 statt .0) und werden unverändert geprüft.
/// </summary>
public static class AliasAnalyzer
{
    public static List<AliasRow> Analyze(BackupData data)
    {
        // Objektbestand einmal als Menge — case-sensitiv, weil ioBroker-IDs es sind.
        var known = new HashSet<string>(data.Objects.Select(o => o.Id), StringComparer.Ordinal);

        var rows = new List<AliasRow>();

        foreach (var o in data.Objects)
        {
            if (o.Type != "state") continue;
            if (!o.Id.StartsWith("alias.", StringComparison.Ordinal)) continue;

            var read = o.AliasRead ?? "";
            var write = o.AliasWrite ?? "";

            // Ein Alias ohne jedes Ziel ist unfertig; er gehört nicht in die Übersicht.
            if (read.Length == 0 && write.Length == 0) continue;

            rows.Add(new AliasRow
            {
                Id = o.Id,
                Name = o.Name,
                ReadTarget = read,
                WriteTarget = write,
                ReadExists = read.Length > 0 && known.Contains(read),
                WriteExists = write.Length > 0 && known.Contains(write),
                ConverterRead = o.ConverterRead ?? "",
                ConverterWrite = o.ConverterWrite ?? ""
            });
        }

        return rows.OrderBy(r => r.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }
}

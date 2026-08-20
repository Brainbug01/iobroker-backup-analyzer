using System.Diagnostics;
using IobBackupAnalyzer.Core;

namespace IobBackupAnalyzer.App;

/// <summary>
/// Gemeinsamer CSV-Export für alle Ergebnistabellen: Speicherdialog, Schreiben und
/// Öffnen des Zielordners im Explorer.
/// </summary>
internal static class CsvExport
{
    public static void Save(IWin32Window owner, string suggestedName,
                            string[] headers, IEnumerable<string[]> rows)
    {
        var list = rows as IList<string[]> ?? rows.ToList();
        if (list.Count == 0)
        {
            MessageBox.Show(owner, "Es gibt keine Einträge zum Exportieren.", "Hinweis",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new SaveFileDialog
        {
            Title = "CSV speichern",
            FileName = suggestedName,
            Filter = "CSV-Datei (*.csv)|*.csv|Alle Dateien (*.*)|*.*",
            DefaultExt = "csv"
        };
        if (dlg.ShowDialog(owner) != DialogResult.OK) return;

        try
        {
            ScriptExporter.WriteCsv(dlg.FileName, headers, list);

            var folder = Path.GetDirectoryName(dlg.FileName);
            if (folder is not null)
                Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Program.LogError("CSV-Export", ex);
            MessageBox.Show(owner, "Die CSV-Datei konnte nicht geschrieben werden:\r\n\r\n" + ex.Message,
                "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

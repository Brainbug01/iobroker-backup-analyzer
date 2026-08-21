// global:: ist hier nötig, nicht Zierde: Der eigene Namespace IobBackupAnalyzer.Avalonia
// verdeckt innerhalb dieser Datei den gleichnamigen Framework-Namespace, sodass
// „using Avalonia;" auf den eigenen zeigen würde und Typen wie Thickness nicht fände.
using global::Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using IobBackupAnalyzer.Core;

namespace IobBackupAnalyzer.Avalonia.Views;

/// <summary>
/// Meldungsfenster und CSV-Export, gemeinsam für alle Ansichten. Avalonia bringt keinen
/// MessageBox-Ersatz mit; für einen Hinweis lohnt keine zusätzliche Abhängigkeit.
/// </summary>
internal static class Dialogs
{
    /// <summary>
    /// Speicherdialog und Schreiben. Geschrieben wird mit <see cref="ScriptExporter.WriteCsv"/>
    /// — derselben Funktion, die auch die Windows-App benutzt, damit beide Fassungen
    /// zeichengleiche Dateien erzeugen.
    /// </summary>
    public static async Task SaveCsvAsync(Control owner, string suggestedName,
                                          string[] headers, IList<string[]> rows)
    {
        var top = TopLevel.GetTopLevel(owner);
        if (top is null) return;

        if (rows.Count == 0)
        {
            await MessageAsync(owner, "Hinweis", "Es gibt keine Einträge zum Exportieren.");
            return;
        }

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "CSV speichern",
            SuggestedFileName = suggestedName,
            DefaultExtension = "csv",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("CSV-Datei") { Patterns = new[] { "*.csv" } }
            }
        });

        var path = file?.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        // Im Speicherdialog steht man oft im Ordner der Backups; ein Klick auf die falsche
        // Zeile übernimmt deren Namen. Eine CSV über ein Archiv zu schreiben, ist immer ein
        // Versehen — und nicht rückgängig zu machen.
        if (ExportPaths.ArchiveWarning(path) is { } warning
            && !await ConfirmAsync(owner, "Sicher?", warning))
            return;

        try
        {
            ScriptExporter.WriteCsv(path, headers, rows);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await MessageAsync(owner, "Fehler",
                "Die CSV-Datei konnte nicht geschrieben werden:\n\n" + ex.Message);
        }
    }

    /// <summary>
    /// Ja/Nein-Rückfrage. „Nein" ist vorbelegt: Diese Frage kommt nur in Fällen, in denen
    /// Weitermachen Erklärungsbedarf hat — dann soll ein versehentliches Enter abbrechen.
    /// </summary>
    public static async Task<bool> ConfirmAsync(Control owner, string title, string message)
    {
        if (TopLevel.GetTopLevel(owner) is not Window window) return false;

        var result = false;
        var yes = new Button { Content = "Ja", Width = 90 };
        var no = new Button { Content = "Nein", Width = 90, IsDefault = true };

        var dlg = new Window
        {
            Title = title,
            Width = 560,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 14,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { yes, no }
                    }
                }
            }
        };

        yes.Click += (_, _) => { result = true; dlg.Close(); };
        no.Click += (_, _) => dlg.Close();

        await dlg.ShowDialog(window);
        return result;
    }

    public static async Task MessageAsync(Control owner, string title, string message)
    {
        if (TopLevel.GetTopLevel(owner) is not Window window) return;

        var ok = new Button
        {
            Content = "OK",
            Width = 90,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var dlg = new Window
        {
            Title = title,
            Width = 520,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 14,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    ok
                }
            }
        };

        ok.Click += (_, _) => dlg.Close();
        await dlg.ShowDialog(window);
    }
}

using System.Runtime.InteropServices;
using System.Text;

namespace IobBackupAnalyzer.App;

/// <summary>
/// Rüstet eine ListView zum Kopieren nach: Rechtsklick-Menü für die getroffene Zelle,
/// die Zeile oder alle markierten Zeilen, dazu Strg+C und Strg+A.
///
/// Ohne das müsste man Werte wie Widget-IDs oder Datenpunkt-IDs abtippen.
/// </summary>
internal static class ListViewCopy
{
    public static void Attach(ListView list)
    {
        // Spalte, die beim Rechtsklick unter dem Mauszeiger lag.
        var hitColumn = 0;

        var menu = new ContextMenuStrip();
        var copyCell = new ToolStripMenuItem("Wert kopieren");
        var copyRow = new ToolStripMenuItem("Zeile kopieren");
        var copySelected = new ToolStripMenuItem("Markierte Zeilen kopieren");
        var copyColumn = new ToolStripMenuItem("Diese Spalte aller markierten Zeilen kopieren");

        menu.Items.AddRange(new ToolStripItem[]
        {
            copyCell, copyRow, new ToolStripSeparator(), copySelected, copyColumn
        });

        copyCell.Click += (_, _) =>
        {
            if (list.SelectedItems.Count > 0)
                Copy(CellText(list.SelectedItems[0], hitColumn));
        };

        copyRow.Click += (_, _) =>
        {
            if (list.SelectedItems.Count > 0)
                Copy(RowText(list.SelectedItems[0]));
        };

        copySelected.Click += (_, _) => Copy(SelectedRowsText(list));

        copyColumn.Click += (_, _) =>
        {
            var sb = new StringBuilder();
            foreach (ListViewItem item in list.SelectedItems)
                sb.AppendLine(CellText(item, hitColumn));
            Copy(sb.ToString().TrimEnd());
        };

        // Rechtsklick: Zeile mit auswählen, falls noch nicht markiert, und Menü beschriften.
        list.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;

            var hit = list.HitTest(e.Location);
            if (hit.Item is null) return;

            hitColumn = hit.Item.SubItems.IndexOf(hit.SubItem);
            if (hitColumn < 0) hitColumn = 0;

            if (!hit.Item.Selected)
            {
                list.SelectedItems.Clear();
                hit.Item.Selected = true;
            }

            var value = CellText(hit.Item, hitColumn);
            var shown = value.Length > 40 ? value[..40] + "…" : value;
            // Ohne den Sortierpfeil, den ListViewSort an den Spaltentitel hängt.
            var columnName = hitColumn < list.Columns.Count
                ? ListViewSort.StripMarker(list.Columns[hitColumn].Text)
                : "Wert";

            copyCell.Text = value.Length == 0
                ? $"{columnName} kopieren (leer)"
                : $"„{shown}\" kopieren";
            copyCell.Enabled = value.Length > 0;

            copySelected.Text = $"Markierte Zeilen kopieren ({list.SelectedItems.Count})";
            copyColumn.Text = $"Spalte „{columnName}\" aller markierten Zeilen kopieren";
        };

        list.KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.C)
            {
                Copy(SelectedRowsText(list));
                e.Handled = true;
            }
            else if (e.Control && e.KeyCode == Keys.A && list.MultiSelect)
            {
                foreach (ListViewItem item in list.Items) item.Selected = true;
                e.Handled = true;
            }
        };

        list.ContextMenuStrip = menu;
    }

    private static string CellText(ListViewItem item, int column) =>
        column >= 0 && column < item.SubItems.Count ? item.SubItems[column].Text : "";

    private static string RowText(ListViewItem item) =>
        string.Join("\t", item.SubItems.Cast<ListViewItem.ListViewSubItem>().Select(s => s.Text));

    private static string SelectedRowsText(ListView list)
    {
        var sb = new StringBuilder();
        foreach (ListViewItem item in list.SelectedItems)
            sb.AppendLine(RowText(item));
        return sb.ToString().TrimEnd();
    }

    private static void Copy(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        try
        {
            Clipboard.SetText(text);
        }
        catch (ExternalException ex)
        {
            // Die Zwischenablage kann kurzzeitig von einem anderen Programm belegt sein.
            Program.LogError("Zwischenablage", ex);
            MessageBox.Show("Die Zwischenablage ist gerade durch ein anderes Programm belegt.\r\n" +
                            "Bitte kurz warten und erneut versuchen.",
                "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}

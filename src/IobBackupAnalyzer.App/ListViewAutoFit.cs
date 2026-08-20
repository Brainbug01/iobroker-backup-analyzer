using System.Runtime.InteropServices;

namespace IobBackupAnalyzer.App;

/// <summary>
/// Rechtsklick auf einen Spaltenkopf macht die Spalte so breit, dass ihr längster Text
/// vollständig hineinpasst — und nur diese eine Spalte.
///
/// <b>Warum nicht der eingebaute Autofit?</b> Ein Doppelklick auf die Trennlinie im
/// Spaltenkopf kann das schon, hört aber ungefähr bei Fensterbreite auf. Bei einem
/// Datenpunkt, der in 28 Views vorkommt, endet die Spalte dann mitten im Text
/// („… | VIS 2: Fir"), und der Rest ist nicht mehr zu sehen. Hier wird stattdessen jede
/// Zeile selbst vermessen; die Spalte darf danach breiter sein als das Fenster — dafür
/// gibt es den waagerechten Rollbalken.
///
/// Dieselbe Geste gibt es in der Avalonia-Fassung (siehe TableLayout dort), damit die
/// Hilfe nicht zwei verschiedene Bedienungen erklären muss.
/// </summary>
internal static class ListViewAutoFit
{
    /// <summary>Hängt die Rechtsklick-Anpassung an die Kopfzeile der Liste.</summary>
    public static void Attach(ListView list)
    {
        // Der Spaltenkopf ist ein eigenes Fenster innerhalb der ListView und bekommt seine
        // Mausklicks selbst — über die ListView-Ereignisse sind sie nicht zu erreichen.
        // Deshalb wird sein Fenster hier mitgelesen.
        if (list.IsHandleCreated) Hook(list);
        else list.HandleCreated += (_, _) => Hook(list);
    }

    private static void Hook(ListView list)
    {
        var header = SendMessage(list.Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
        if (header != IntPtr.Zero) _ = new HeaderWatcher(list, header);
    }

    /// <summary>
    /// Breite, die eine Spalte für ihren Inhalt braucht: der längste Text aller Zeilen,
    /// die Überschrift eingerechnet, plus etwas Luft für Ränder und Sortierpfeil.
    /// </summary>
    private static int RequiredWidth(ListView list, int columnIndex)
    {
        var widest = Measure(list.Columns[columnIndex].Text, list.Font) + 20;

        foreach (ListViewItem item in list.Items)
        {
            if (columnIndex >= item.SubItems.Count) continue;
            var text = item.SubItems[columnIndex].Text;
            if (text.Length == 0) continue;

            var w = Measure(text, list.Font);
            if (w > widest) widest = w;
        }

        return widest + 12;
    }

    /// <summary>
    /// Breite eines Textes in einer Zeile.
    ///
    /// Die Vorgaben sind nicht schmückendes Beiwerk: Ohne sie nimmt die Messung an, der
    /// Text dürfe umbrechen, und liefert für die 500 Zeichen eines vielfach verwendeten
    /// Datenpunkts eine viel zu kleine Breite — die Spalte endete dann mitten im Text.
    /// Die vorgegebene Fläche muss großzügig sein, sonst begrenzt sie das Ergebnis.
    /// </summary>
    private static int Measure(string text, Font font) =>
        TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue),
                                 TextFormatFlags.NoPrefix
                                 | TextFormatFlags.SingleLine
                                 | TextFormatFlags.NoPadding).Width;

    /// <summary>
    /// Liest die Mausklicks der Kopfzeile mit. Ein eigenes Fenster ohne .NET-Ereignisse —
    /// <see cref="NativeWindow"/> ist der vorgesehene Weg, sich dazwischenzuhängen.
    /// </summary>
    private sealed class HeaderWatcher : NativeWindow
    {
        private readonly ListView _list;

        public HeaderWatcher(ListView list, IntPtr headerHandle)
        {
            _list = list;
            AssignHandle(headerHandle);
            // Wird die Liste zerstört, verschwindet auch die Kopfzeile — dann loslassen,
            // sonst zeigt das Handle ins Leere.
            list.HandleDestroyed += (_, _) => ReleaseHandle();
        }

        protected override void WndProc(ref Message m)
        {
            // Erst nach dem Loslassen: Beim Drücken wäre noch offen, ob der Nutzer die
            // Spalte in Wahrheit verschieben will.
            if (m.Msg == WM_RBUTTONUP)
            {
                var x = (short)(m.LParam.ToInt32() & 0xFFFF);
                var column = ColumnAt(x);
                if (column >= 0)
                {
                    _list.Columns[column].Width = RequiredWidth(_list, column);
                    return;   // nicht weiterreichen: sonst folgt das übliche Kontextmenü
                }
            }

            base.WndProc(ref m);
        }

        /// <summary>
        /// Spaltenindex an der Klickposition. Gefragt wird die Kopfzeile selbst, weil sie
        /// als Einzige weiß, wo ihre Felder gerade sitzen — Spalten lassen sich
        /// verschieben, und die Liste kann waagerecht gerollt sein.
        /// </summary>
        private int ColumnAt(int x)
        {
            for (var display = 0; display < _list.Columns.Count; display++)
            {
                var rect = new RECT();
                if (SendMessage(Handle, HDM_GETITEMRECT, (IntPtr)display,
                                ref rect) == IntPtr.Zero) continue;

                if (x < rect.Left || x >= rect.Right) continue;

                // Die Kopfzeile zählt in Anzeigereihenfolge, die Spaltenliste nicht.
                for (var i = 0; i < _list.Columns.Count; i++)
                    if (_list.Columns[i].DisplayIndex == display)
                        return i;
            }

            return -1;
        }
    }

    private const int LVM_GETHEADER = 0x1000 + 31;
    private const int HDM_GETITEMRECT = 0x1200 + 7;
    private const int WM_RBUTTONUP = 0x0205;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref RECT lParam);
}

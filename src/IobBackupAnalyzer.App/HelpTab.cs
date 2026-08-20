using IobBackupAnalyzer.Core;

namespace IobBackupAnalyzer.App;

/// <summary>
/// Gerenderter Fließtext aus <see cref="HelpBlock"/>-Absätzen. Zwei Tabs benutzen ihn:
/// die In-App-Hilfe (<see cref="HelpContent.Blocks"/>) und der Änderungsverlauf
/// (<see cref="ChangelogContent.Blocks"/>). Statischer Text, immer verfügbar —
/// unabhängig davon, ob ein Backup geladen ist.
///
/// Der Text selbst steht im Core und wird mit der Avalonia-Fassung geteilt; hier steht
/// nur die Formatierung.
/// </summary>
public sealed class HelpTab : UserControl
{
    private readonly RichTextBox _text = new();
    private readonly IReadOnlyList<HelpBlock> _blocks;

    private readonly Font _h1 = new("Segoe UI", 13F, FontStyle.Bold);
    private readonly Font _h2 = new("Segoe UI", 10.5F, FontStyle.Bold);
    private readonly Font _body = new("Segoe UI", 9.75F, FontStyle.Regular);

    /// <param name="blocks">Die anzuzeigenden Absätze; ohne Angabe die Hilfe.</param>
    public HelpTab(IReadOnlyList<HelpBlock>? blocks = null)
    {
        _blocks = blocks ?? HelpContent.Blocks;
        BuildUi();
        Populate();
    }


    private void BuildUi()
    {
        Padding = new Padding(8);

        _text.Dock = DockStyle.Fill;
        _text.ReadOnly = true;
        _text.BorderStyle = BorderStyle.None;
        _text.BackColor = SystemColors.Window;
        _text.DetectUrls = false;
        _text.WordWrap = true;
        _text.ScrollBars = RichTextBoxScrollBars.Vertical;
        // Etwas Innenabstand über einen umgebenden Panel-Rand.
        var frame = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 8, 12, 8), BackColor = SystemColors.Window };
        frame.Controls.Add(_text);

        Controls.Add(frame);
    }

    private void H1(string s) { _text.SelectionFont = _h1; _text.SelectionColor = SystemColors.ControlText; _text.AppendText(s + "\n"); }
    private void H2(string s) { _text.AppendText("\n"); _text.SelectionFont = _h2; _text.SelectionColor = Color.FromArgb(0, 70, 130); _text.AppendText(s + "\n"); }
    private void P(string s)  { _text.SelectionFont = _body; _text.SelectionColor = SystemColors.ControlText; _text.AppendText(s + "\n"); }

    private void Populate()
    {
        _text.Clear();

        foreach (var block in _blocks)
        {
            switch (block.Kind)
            {
                case HelpBlockKind.Title: H1(block.Text); break;
                case HelpBlockKind.Heading: H2(block.Text); break;
                default: P(block.Text); break;
            }
        }

        _text.SelectionStart = 0;
        _text.ScrollToCaret();
    }
}

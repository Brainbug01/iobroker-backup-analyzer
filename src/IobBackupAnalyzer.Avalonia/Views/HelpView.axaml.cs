using global::Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using IobBackupAnalyzer.Core;

namespace IobBackupAnalyzer.Avalonia.Views;

/// <summary>
/// Gerenderter Fließtext aus <see cref="HelpBlock"/>-Absätzen: die In-App-Hilfe und der
/// Änderungsverlauf teilen sich diese Ansicht. Der Text stammt aus dem Core — derselben
/// Quelle wie in der Windows-Fassung —, hier steht nur die Darstellung.
/// </summary>
public partial class HelpView : UserControl
{
    /// <summary>Parameterlos für XAML: zeigt die Hilfe.</summary>
    public HelpView() : this(HelpContent.Blocks) { }

    /// <summary>Für den Änderungsverlauf mit <see cref="ChangelogContent.Blocks"/> aufrufen.</summary>
    public HelpView(IReadOnlyList<HelpBlock> blocks)
    {
        AvaloniaXamlLoader.Load(this);

        var panel = this.FindControl<StackPanel>("HelpBlocks")!;

        foreach (var block in blocks)
        {
            var text = new TextBlock
            {
                Text = block.Text,
                TextWrapping = TextWrapping.Wrap
            };

            switch (block.Kind)
            {
                case HelpBlockKind.Title:
                    text.FontSize = 20;
                    text.FontWeight = FontWeight.Bold;
                    text.Margin = new Thickness(0, 0, 0, 8);
                    break;

                case HelpBlockKind.Heading:
                    text.FontSize = 15;
                    text.FontWeight = FontWeight.SemiBold;
                    // Derselbe Blauton wie in der WinForms-Fassung, auf hellem wie
                    // dunklem Hintergrund lesbar.
                    text.Foreground = new SolidColorBrush(Color.Parse("#3D8BC9"));
                    text.Margin = new Thickness(0, 18, 0, 6);
                    break;

                default:
                    text.Margin = new Thickness(0, 0, 0, 8);
                    text.LineHeight = 20;
                    break;
            }

            panel.Children.Add(text);
        }
    }
}

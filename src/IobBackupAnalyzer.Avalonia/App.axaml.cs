using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace IobBackupAnalyzer.Avalonia;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Eine als Argument übergebene Datei direkt laden — so funktioniert
            // „Öffnen mit" bzw. das Ziehen auf das Programmsymbol.
            var startFile = desktop.Args is { Length: > 0 } a && File.Exists(a[0]) ? a[0] : null;
            desktop.MainWindow = new MainWindow(startFile);
        }

        base.OnFrameworkInitializationCompleted();
    }
}

using System.Windows;
using EDIDReader.App.Services;

namespace EDIDReader.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        PortableStorageService.EnsureDirectories();
        ThemeService.Initialize();
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ThemeService.Shutdown();
        base.OnExit(e);
    }
}

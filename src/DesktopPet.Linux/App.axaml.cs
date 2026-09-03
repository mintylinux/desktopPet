using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace DesktopPet.Linux;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // No visible main window: this app only shows pet windows + a tray icon, and
            // should keep running even though no "main" window is ever opened/closed.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            StartUp.ExitAction = () => desktop.Shutdown();

            Program.SettingsWindow = new SettingsWindow();
            ScreenInfo.Refresh(Program.SettingsWindow.Screens);

            var trayIcon = new ProcessIcon();
            trayIcon.Display();
            Program.Mainthread = new StartUp(trayIcon);

            Program.SettingsWindow.Show();
        }

        base.OnFrameworkInitializationCompleted();
    }
}

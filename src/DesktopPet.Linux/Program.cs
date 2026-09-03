using Avalonia;
using System;
using System.IO;

namespace DesktopPet.Linux;

class Program
{
    /// <summary>Local data / settings store, shared across the app (mirrors the original Program.MyData).</summary>
    public static LocalData.LocalData MyData = null!;

    /// <summary>Main application controller (mirrors the original Program.Mainthread).</summary>
    public static StartUp Mainthread = null!;

    /// <summary>The settings/pet-picker window (hidden to tray rather than closed).</summary>
    public static SettingsWindow SettingsWindow = null!;

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        string storageFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "desktoppet");
        MyData = new LocalData.LocalData(storageFolder, AppContext.BaseDirectory);

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}

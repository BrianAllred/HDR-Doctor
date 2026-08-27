using Avalonia;
using System;

namespace HdrDoctor.App;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        App.Startup = StartupOptions.Parse(args);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}

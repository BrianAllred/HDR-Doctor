using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HdrDoctor.App.Services;
using HdrDoctor.App.ViewModels;
using HdrDoctor.App.Views;

namespace HdrDoctor.App;

public partial class App : Application
{
    private AppServices? _services;

    /// <summary>
    /// Options parsed from the command line before Avalonia starts.
    /// </summary>
    public static StartupOptions Startup { get; set; } = StartupOptions.None;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _services = new AppServices();

            // The dialog service needs the window and the view model needs the dialog
            // service, so the window is constructed before its DataContext.
            var window = new MainWindow();
            var viewModel = new MainViewModel(_services, new DialogService(window));
            window.DataContext = viewModel;

            // Applied once the window is up so the folder picker and any error dialog
            // have a parent to attach to.
            window.Opened += async (_, _) =>
            {
                await viewModel.ApplyStartupOptionsAsync(Startup);
#if !DEBUG
                await viewModel.CheckForUpdatesAsync();
#endif
            };

            desktop.MainWindow = window;
            desktop.Exit += (_, _) => _services.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}

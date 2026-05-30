using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Boxwright.App.Services;
using Boxwright.App.ViewModels;
using Boxwright.App.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Boxwright.App;

/// <summary>
/// The Avalonia <see cref="Application"/>. Builds the DI container (the composition
/// root) and shows the main window wired to its view model.
/// </summary>
public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            ServiceCollection services = new();
            ServiceConfiguration.Register(services);
            ServiceProvider provider = services.BuildServiceProvider();

            ILogger<App> logger = provider.GetRequiredService<ILogger<App>>();
            logger.LogInformation("Boxwright starting; logs at {LogFile}.", AppPaths.AppLogFile);
            InstallGlobalExceptionLogging(provider.GetRequiredService<ILoggerFactory>());

            desktop.MainWindow = new MainWindow
            {
                DataContext = provider.GetRequiredService<MainWindowViewModel>(),
            };

            // Dispose singletons (sockets, processes, log file) when the app shuts down.
            desktop.Exit += (_, _) =>
            {
                logger.LogInformation("Boxwright exiting.");
                provider.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void InstallGlobalExceptionLogging(ILoggerFactory loggerFactory)
    {
        ILogger logger = loggerFactory.CreateLogger("Boxwright.UnhandledException");

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            logger.LogCritical(e.ExceptionObject as Exception, "Unhandled exception (terminating={Terminating}).", e.IsTerminating);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            logger.LogError(e.Exception, "Unobserved task exception.");
            e.SetObserved();
        };
    }
}

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Boxwright.App.ViewModels;
using Boxwright.App.Views;
using Microsoft.Extensions.DependencyInjection;

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

            desktop.MainWindow = new MainWindow
            {
                DataContext = provider.GetRequiredService<MainWindowViewModel>(),
            };

            // Dispose singletons (sockets, processes) when the app shuts down.
            desktop.Exit += (_, _) => provider.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}

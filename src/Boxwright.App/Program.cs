using Avalonia;

namespace Boxwright.App;

/// <summary>
/// Desktop entry point. Configures Avalonia and runs the classic desktop
/// (single main window) lifetime. Application wiring lives in
/// <see cref="App.OnFrameworkInitializationCompleted"/>, not here.
/// </summary>
internal static class Program
{
    // STAThread is required by Avalonia on Windows (clipboard / drag-drop / COM).
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // Named exactly this and kept public: the Avalonia XAML previewer looks it up.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

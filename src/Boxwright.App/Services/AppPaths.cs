namespace Boxwright.App.Services;

/// <summary>Well-known cross-platform paths for Boxwright app data.</summary>
internal static class AppPaths
{
    /// <summary>
    /// Directory for Boxwright's own diagnostics logs — app-wide, distinct from each VM's
    /// per-VM <c>qemu.log</c>. Under the user's local app data (e.g. %LOCALAPPDATA%\Boxwright\logs).
    /// </summary>
    public static string LogsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Boxwright", "logs");

    /// <summary>The app-wide log file path.</summary>
    public static string AppLogFile => Path.Combine(LogsDirectory, "boxwright.log");
}

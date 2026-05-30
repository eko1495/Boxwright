using System.ComponentModel;
using System.Diagnostics;

namespace Boxwright.App.Services;

/// <summary>
/// Opens a folder in the desktop file manager via the OS shell association — Explorer on
/// Windows, Finder on macOS, the default file manager (xdg-open) on Linux. Cross-platform
/// (Directive 4) and best-effort: revealing a folder is a convenience, so shell failures
/// are swallowed rather than surfaced as a crash.
/// </summary>
internal sealed class ShellFolderOpener : IFolderOpener
{
    /// <inheritdoc />
    public void OpenFolder(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            // The logs folder may not exist until the first log line is written.
            Directory.CreateDirectory(path);

            // UseShellExecute=true asks the OS to open the directory with its file manager on
            // every platform (ShellExecute on Windows; open/xdg-open on macOS/Linux). We don't
            // manage the file-manager process, so dispose our handle immediately.
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or PlatformNotSupportedException)
        {
            // Best-effort: never crash the UI because the shell could not open a folder.
        }
    }
}

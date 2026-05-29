namespace Boxwright.Core;

/// <summary>Locates the external <c>remote-viewer</c> (virt-viewer) executable.</summary>
public interface IRemoteViewerLocator
{
    /// <summary>Returns the full path to <c>remote-viewer</c>, or <see langword="null"/> if not found.</summary>
    string? Locate();
}

/// <summary>
/// The default <see cref="IRemoteViewerLocator"/>: looks in the bundled directory,
/// then the system PATH, then (on Windows) the default <c>VirtViewer*</c> install
/// folders under Program Files.
/// </summary>
public sealed class RemoteViewerLocator : IRemoteViewerLocator
{
    private readonly string? _bundledDirectory;

    /// <summary>Creates a locator. Pass the bundled virt-viewer directory in production; null for dev.</summary>
    public RemoteViewerLocator(string? bundledDirectory = null) => _bundledDirectory = bundledDirectory;

    /// <inheritdoc />
    public string? Locate()
    {
        string executable = OperatingSystem.IsWindows() ? "remote-viewer.exe" : "remote-viewer";

        if (_bundledDirectory is not null)
        {
            string bundled = Path.Combine(_bundledDirectory, executable);
            if (File.Exists(bundled))
            {
                return bundled;
            }
        }

        string? onPath = FindOnPath(executable);
        if (onPath is not null)
        {
            return onPath;
        }

        return OperatingSystem.IsWindows() ? FindInWindowsInstallFolders(executable) : null;
    }

    private static string? FindOnPath(string executable)
    {
        string? pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (pathVariable is null)
        {
            return null;
        }

        foreach (string directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string candidate = Path.Combine(directory, executable);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? FindInWindowsInstallFolders(string executable)
    {
        string[] programFilesRoots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        ];

        foreach (string root in programFilesRoots)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                continue;
            }

            foreach (string installDir in Directory.GetDirectories(root, "VirtViewer*"))
            {
                string candidate = Path.Combine(installDir, "bin", executable);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}

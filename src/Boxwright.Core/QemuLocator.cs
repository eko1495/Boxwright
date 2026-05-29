namespace Boxwright.Core;

/// <summary>
/// Resolves the paths of QEMU binaries (<c>qemu-system-&lt;arch&gt;</c> and
/// <c>qemu-img</c>). Production passes the bundled directory (ADR-0007); in dev,
/// with no bundled directory, it falls back to the system PATH (and, on Windows,
/// the default <c>C:\Program Files\qemu</c> install location).
/// </summary>
public sealed class QemuLocator
{
    private readonly string? _bundledDirectory;

    /// <summary>Creates a locator. Pass the bundled QEMU directory in production; null for dev (PATH lookup).</summary>
    public QemuLocator(string? bundledDirectory = null) => _bundledDirectory = bundledDirectory;

    /// <summary>Resolves the full path to <c>qemu-system-&lt;arch&gt;</c>.</summary>
    /// <exception cref="QemuNotFoundException">The binary could not be found.</exception>
    public string ResolveSystemEmulator(string arch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(arch);
        return Resolve($"qemu-system-{arch}");
    }

    /// <summary>Resolves the full path to <c>qemu-img</c>.</summary>
    /// <exception cref="QemuNotFoundException">The binary could not be found.</exception>
    public string ResolveImageTool() => Resolve("qemu-img");

    private string Resolve(string baseName)
    {
        string executable = OperatingSystem.IsWindows() ? baseName + ".exe" : baseName;

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

        if (OperatingSystem.IsWindows())
        {
            string windowsDefault = Path.Combine(@"C:\Program Files\qemu", executable);
            if (File.Exists(windowsDefault))
            {
                return windowsDefault;
            }
        }

        throw new QemuNotFoundException($"Could not locate '{executable}'. Checked the bundled directory and the system PATH.");
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
}

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

    /// <summary>
    /// Resolves the split OVMF/edk2 UEFI firmware (read-only CODE + a VARS/NVRAM template) for
    /// <paramref name="arch"/>, searching beside the QEMU binary and in common system locations.
    /// </summary>
    /// <exception cref="QemuNotFoundException">No UEFI firmware was found.</exception>
    public UefiFirmware ResolveUefiFirmware(string arch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(arch);
        string emulator = ResolveSystemEmulator(arch);
        (string[] codeNames, string[] varsNames) = FirmwareNames(arch);

        foreach (string directory in FirmwareSearchDirectories(emulator))
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            string? code = codeNames.Select(name => Path.Combine(directory, name)).FirstOrDefault(File.Exists);
            string? vars = varsNames.Select(name => Path.Combine(directory, name)).FirstOrDefault(File.Exists);
            if (code is not null && vars is not null)
            {
                return new UefiFirmware(code, vars);
            }
        }

        throw new QemuNotFoundException(
            $"Could not locate UEFI firmware (OVMF) for '{arch}'. Looked next to the QEMU binary and in common system locations.");
    }

    // x86_64's edk2 CODE pairs with the i386 VARS template (QEMU's edk2 packaging); OVMF_* are
    // the Linux distro names. aarch64 has its own pair. Others fall back to the x86_64 set.
    private static (string[] Code, string[] Vars) FirmwareNames(string arch) => arch switch
    {
        "aarch64" => (["edk2-aarch64-code.fd"], ["edk2-arm-vars.fd"]),
        _ => (
            ["edk2-x86_64-code.fd", "OVMF_CODE_4M.fd", "OVMF_CODE.fd"],
            ["edk2-i386-vars.fd", "OVMF_VARS_4M.fd", "OVMF_VARS.fd"]),
    };

    private IEnumerable<string> FirmwareSearchDirectories(string emulatorPath)
    {
        string? emulatorDir = Path.GetDirectoryName(emulatorPath);
        if (emulatorDir is not null)
        {
            yield return Path.Combine(emulatorDir, "share");                 // Windows/weilnetz + bundled layout
            yield return Path.Combine(emulatorDir, "..", "share", "qemu");    // some *nix install layouts
        }

        if (_bundledDirectory is not null)
        {
            yield return Path.Combine(_bundledDirectory, "share");
        }

        // Common system locations (Linux distros, Homebrew on macOS).
        yield return "/usr/share/OVMF";
        yield return "/usr/share/edk2/ovmf";
        yield return "/usr/share/edk2/x64";
        yield return "/usr/share/qemu";
        yield return "/opt/homebrew/share/qemu";
        yield return "/usr/local/share/qemu";
    }

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

/// <summary>Located UEFI firmware: a read-only <paramref name="CodePath"/> and a VARS/NVRAM <paramref name="VarsTemplatePath"/> to copy per VM.</summary>
public sealed record UefiFirmware(string CodePath, string VarsTemplatePath);

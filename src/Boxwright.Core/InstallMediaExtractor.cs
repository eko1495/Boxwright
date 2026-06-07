using System.Text.RegularExpressions;

namespace Boxwright.Core;

/// <summary>
/// Extracts the installer kernel + initrd from an Ubuntu (casper) live ISO and builds the
/// <see cref="InstallBootConfig"/> that boots it hands-free (ADR-0013 Phase B). Reads the ISO via
/// <see cref="IsoMedia"/> (DiscUtils) — pure managed code, no external tool — so it works the same on
/// Windows, macOS, and Linux. The ISO's own kernel command line (from <c>/boot/grub/grub.cfg</c>) is
/// preserved (it carries the desktop ISO's <c>layerfs-path=…</c>), with the caller's
/// <c>autoinstall</c> arguments prepended so the installer runs non-interactively.
/// </summary>
public sealed partial class InstallMediaExtractor : IInstallMediaExtractor
{
    /// <summary>The extracted kernel file name written into the VM folder.</summary>
    public const string KernelFileName = "vmlinuz";

    /// <summary>The extracted initrd file name written into the VM folder.</summary>
    public const string InitrdFileName = "initrd";

    // DiscUtils file-system paths use backslash separators. Ubuntu live ISOs keep the kernel/initrd under
    // /casper; the initrd is occasionally compressed with a suffix, so probe the known variants.
    private const string KernelIsoPath = @"casper\vmlinuz";
    private static readonly string[] InitrdIsoPaths = [@"casper\initrd", @"casper\initrd.lz", @"casper\initrd.gz"];
    private const string GrubCfgIsoPath = @"boot\grub\grub.cfg";

    /// <inheritdoc />
    public InstallBootConfig Extract(string isoPath, string vmFolderPath, string seedArgs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(isoPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(vmFolderPath);

        Directory.CreateDirectory(vmFolderPath);

        using IsoMedia iso = IsoMedia.Open(isoPath);

        if (!iso.FileExists(KernelIsoPath))
        {
            throw new InstallMediaException(
                $"'{Path.GetFileName(isoPath)}' has no installer kernel at /casper/vmlinuz — it may not be an Ubuntu (casper) live ISO.");
        }

        iso.CopyFile(KernelIsoPath, Path.Combine(vmFolderPath, KernelFileName));

        string? initrdSource = Array.Find(InitrdIsoPaths, iso.FileExists)
            ?? throw new InstallMediaException($"'{Path.GetFileName(isoPath)}' has no installer initrd under /casper.");
        iso.CopyFile(initrdSource, Path.Combine(vmFolderPath, InitrdFileName));

        string grubCfg = iso.FileExists(GrubCfgIsoPath) ? iso.ReadText(GrubCfgIsoPath) : string.Empty;

        return new InstallBootConfig
        {
            KernelFile = KernelFileName,
            InitrdFile = InitrdFileName,
            Append = BuildAppend(seedArgs, grubCfg),
        };
    }

    /// <summary>
    /// Composes the kernel command line: <paramref name="seedArgs"/> (e.g. <c>autoinstall ds=nocloud</c>)
    /// followed by the ISO's own <c>linux /casper/vmlinuz …</c> arguments parsed from
    /// <paramref name="grubCfgContent"/> — preserving the desktop ISO's <c>layerfs-path=…</c>. A grub.cfg
    /// without a matching line yields just <paramref name="seedArgs"/>.
    /// </summary>
    public static string BuildAppend(string seedArgs, string grubCfgContent)
    {
        Match match = LinuxLine().Match(grubCfgContent ?? string.Empty);
        string baseArgs = match.Success ? match.Groups["args"].Value.Trim() : string.Empty;
        string combined = string.IsNullOrWhiteSpace(baseArgs) ? seedArgs : $"{seedArgs} {baseArgs}";
        return combined.Trim();
    }

    [GeneratedRegex(@"linux\s+/casper/vmlinuz\s+(?<args>[^\r\n]*)", RegexOptions.IgnoreCase)]
    private static partial Regex LinuxLine();
}

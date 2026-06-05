using System.Text.RegularExpressions;
using DiscUtils.Iso9660;

namespace Boxwright.Core;

/// <summary>
/// Extracts the installer kernel + initrd from an Ubuntu (casper) live ISO and builds the
/// <see cref="InstallBootConfig"/> that boots it hands-free (ADR-0013 Phase B). Reads the ISO with
/// DiscUtils' <see cref="CDReader"/> — pure managed code, no external tool — so it works the same on
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

        FileStream iso;
        try
        {
            iso = new FileStream(isoPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InstallMediaException($"Couldn't open the installer ISO '{Path.GetFileName(isoPath)}': {ex.Message}", ex);
        }

        using (iso)
        {
            CDReader reader;
            try
            {
                reader = new CDReader(iso, joliet: true);
            }
            catch (Exception ex)
            {
                // Any parse failure means it isn't a usable ISO9660 image.
                throw new InstallMediaException($"'{Path.GetFileName(isoPath)}' is not a readable ISO9660 image: {ex.Message}", ex);
            }

            if (!reader.FileExists(KernelIsoPath))
            {
                throw new InstallMediaException(
                    $"'{Path.GetFileName(isoPath)}' has no installer kernel at /casper/vmlinuz — it may not be an Ubuntu (casper) live ISO.");
            }

            ExtractFile(reader, KernelIsoPath, Path.Combine(vmFolderPath, KernelFileName));

            string? initrdSource = Array.Find(InitrdIsoPaths, reader.FileExists)
                ?? throw new InstallMediaException($"'{Path.GetFileName(isoPath)}' has no installer initrd under /casper.");
            ExtractFile(reader, initrdSource, Path.Combine(vmFolderPath, InitrdFileName));

            string grubCfg = reader.FileExists(GrubCfgIsoPath) ? ReadText(reader, GrubCfgIsoPath) : string.Empty;

            return new InstallBootConfig
            {
                KernelFile = KernelFileName,
                InitrdFile = InitrdFileName,
                Append = BuildAppend(seedArgs, grubCfg),
            };
        }
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

    private static void ExtractFile(CDReader reader, string isoPath, string destination)
    {
        using Stream source = reader.OpenFile(isoPath, FileMode.Open, FileAccess.Read);
        using var dest = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        source.CopyTo(dest);
    }

    private static string ReadText(CDReader reader, string path)
    {
        using Stream stream = reader.OpenFile(path, FileMode.Open, FileAccess.Read);
        using var text = new StreamReader(stream);
        return text.ReadToEnd();
    }

    [GeneratedRegex(@"linux\s+/casper/vmlinuz\s+(?<args>[^\r\n]*)", RegexOptions.IgnoreCase)]
    private static partial Regex LinuxLine();
}

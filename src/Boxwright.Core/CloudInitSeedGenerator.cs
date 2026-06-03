using System.Text;
using DiscUtils;
using DiscUtils.Fat;

namespace Boxwright.Core;

/// <summary>
/// Writes a cloud-init <b>NoCloud</b> seed as a small <b>FAT</b> image (volume label <c>CIDATA</c>)
/// holding <c>user-data</c> (the Ubuntu autoinstall config) and <c>meta-data</c>. Built in pure
/// managed code via DiscUtils — no external tool (<c>genisoimage</c>/<c>cloud-localds</c>) is needed,
/// so it works the same on Windows, macOS, and Linux. Attached to the VM as a tiny raw disk, its
/// <c>CIDATA</c> label is what cloud-init probes for (see ADR-0013).
/// </summary>
/// <remarks>
/// FAT — not ISO9660 — because the only managed ISO writer (DiscUtils) mangles extension-less names
/// (it appends a trailing <c>.</c> → <c>user-data.</c>) and cannot emit Rock Ridge, so cloud-init
/// would not find the files. A FAT volume preserves the exact names, and NoCloud explicitly
/// supports a FAT-labelled seed (this is what <c>cloud-localds</c> produces).
/// </remarks>
public sealed class CloudInitSeedGenerator : ISeedGenerator
{
    /// <summary>The seed image file name written into the VM folder.</summary>
    public const string SeedFileName = "seed.img";

    /// <summary>The NoCloud volume label cloud-init probes for.</summary>
    public const string VolumeLabel = "CIDATA";

    /// <inheritdoc />
    public string Generate(UnattendedAnswers answers, string vmFolderPath)
    {
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentException.ThrowIfNullOrWhiteSpace(vmFolderPath);

        string userData = AutoinstallUserData.Build(answers);
        string metaData = AutoinstallUserData.MetaData(Guid.NewGuid().ToString());

        Directory.CreateDirectory(vmFolderPath);
        string path = Path.Combine(vmFolderPath, SeedFileName);
        using (var image = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        using (FatFileSystem fs = FatFileSystem.FormatFloppy(image, FloppyDiskType.HighDensity, VolumeLabel))
        {
            WriteFile(fs, "user-data", userData);
            WriteFile(fs, "meta-data", metaData);
        }

        return path;
    }

    private static void WriteFile(FatFileSystem fs, string name, string content)
    {
        using Stream file = fs.OpenFile(name, FileMode.Create, FileAccess.Write);
        file.Write(Encoding.UTF8.GetBytes(content));
    }
}

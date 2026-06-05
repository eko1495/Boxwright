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
    public string Generate(UnattendedAnswers answers, string vmFolderPath, SeedProfile profile = SeedProfile.InstallerAutoinstall)
    {
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentException.ThrowIfNullOrWhiteSpace(vmFolderPath);

        // The user-data differs by profile; the NoCloud meta-data (just an instance id) is the same.
        string userData = profile switch
        {
            SeedProfile.CloudImage => CloudImageUserData.Build(answers),
            _ => AutoinstallUserData.Build(answers),
        };
        string metaData = AutoinstallUserData.MetaData(Guid.NewGuid().ToString());

        Directory.CreateDirectory(vmFolderPath);
        string path = Path.Combine(vmFolderPath, SeedFileName);
        using (var image = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        using (FatFileSystem fs = FatFileSystem.FormatFloppy(image, FloppyDiskType.HighDensity, VolumeLabel))
        {
            WriteFile(fs, "user-data", userData);
            WriteFile(fs, "meta-data", metaData);
        }

        WriteRootDirectoryVolumeLabel(path, VolumeLabel);

        return path;
    }

    private static void WriteFile(FatFileSystem fs, string name, string content)
    {
        using Stream file = fs.OpenFile(name, FileMode.Create, FileAccess.Write);
        file.Write(Encoding.UTF8.GetBytes(content));
    }

    /// <summary>
    /// Adds a root-directory volume-label entry (FAT attribute <c>0x08</c>) to the just-written image.
    /// </summary>
    /// <remarks>
    /// DiscUtils writes the label only into the BPB boot sector. Linux <c>blkid</c> exposes that as
    /// <c>LABEL_FATBOOT</c>, but udev creates <c>/dev/disk/by-label/&lt;LABEL&gt;</c> only from the real
    /// <c>LABEL</c> — the root-directory volume-label entry, which DiscUtils never writes. Without it the
    /// <c>CIDATA</c> by-label link never appears, so cloud-init's NoCloud probe can't find the seed and the
    /// guest boots with no datasource (verified end-to-end against an Ubuntu 24.04 cloud image). We add the
    /// entry ourselves. <see cref="FatFileSystem.FormatFloppy"/> always produces a FAT12 volume with a
    /// fixed-size root directory, so the on-disk offset is computed straight from the BPB.
    /// </remarks>
    private static void WriteRootDirectoryVolumeLabel(string path, string label)
    {
        using var image = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        byte[] bpb = new byte[24];
        image.Position = 0;
        image.ReadExactly(bpb);
        int bytesPerSector = bpb[11] | (bpb[12] << 8);
        int reservedSectors = bpb[14] | (bpb[15] << 8);
        int fatCount = bpb[16];
        int rootEntryCount = bpb[17] | (bpb[18] << 8);
        int sectorsPerFat = bpb[22] | (bpb[23] << 8);

        if (bytesPerSector == 0 || rootEntryCount == 0)
        {
            throw new InvalidOperationException("Unexpected FAT geometry: the seed must be a fixed-root-dir FAT12 volume.");
        }

        long rootDirStart = (long)(reservedSectors + (fatCount * sectorsPerFat)) * bytesPerSector;
        byte[] entry = CreateVolumeLabelEntry(label);

        byte[] slot = new byte[DirectoryEntrySize];
        for (int i = 0; i < rootEntryCount; i++)
        {
            long position = rootDirStart + ((long)i * DirectoryEntrySize);
            image.Position = position;
            image.ReadExactly(slot);

            // 0x00 = never used (and end-of-directory marker), 0xE5 = deleted — either is free to reuse.
            if (slot[0] is 0x00 or 0xE5)
            {
                image.Position = position;
                image.Write(entry);
                return;
            }
        }

        throw new InvalidOperationException("No free root-directory slot for the volume-label entry.");
    }

    private const int DirectoryEntrySize = 32;

    // A 32-byte FAT directory entry tagged as the volume label: the 11-byte name holds the label
    // (space-padded, upper-cased ASCII) and the attribute byte is ATTR_VOLUME_ID (0x08).
    private static byte[] CreateVolumeLabelEntry(string label)
    {
        const byte AttrVolumeId = 0x08;
        byte[] entry = new byte[DirectoryEntrySize];
        string name = label.ToUpperInvariant();
        for (int i = 0; i < 11; i++)
        {
            entry[i] = (byte)(i < name.Length ? name[i] : ' ');
        }

        entry[11] = AttrVolumeId;
        return entry;
    }
}

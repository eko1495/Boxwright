using System.Text;
using DiscUtils.Iso9660;

namespace Boxwright.Core;

/// <summary>
/// Writes the Windows unattended-install seed as a small <b>ISO9660 (Joliet)</b> CD holding
/// <c>Autounattend.xml</c> at its root (built by <see cref="AutounattendXml"/>). Attached to the VM as
/// an extra CD-ROM, Windows Setup auto-scans removable-media roots and applies it with no interaction.
/// Built in pure managed code via DiscUtils — no external tool — so it works the same on Windows, macOS,
/// and Linux. Joliet preserves the exact <c>Autounattend.xml</c> name (it has an extension, so the
/// ISO9660 trailing-dot issue that blocked the cloud-init seed does not apply here — see ADR-0013/0015).
/// </summary>
public sealed class AutounattendSeedGenerator : IAutounattendSeedGenerator
{
    /// <summary>The seed ISO file name written into the VM folder.</summary>
    public const string SeedFileName = "autounattend.iso";

    /// <summary>The ISO volume label (Windows ignores it — Setup scans the root regardless).</summary>
    public const string VolumeLabel = "UNATTEND";

    /// <inheritdoc />
    public string Generate(UnattendedAnswers answers, WindowsInstallOptions options, bool uefi, string vmFolderPath)
    {
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(vmFolderPath);

        string xml = AutounattendXml.Build(answers, options, uefi);
        // UTF-8 without a BOM — the document declares utf-8 and Setup prefers no BOM.
        byte[] xmlBytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(xml);

        var builder = new CDBuilder { UseJoliet = true, VolumeIdentifier = VolumeLabel };
        builder.AddFile(AutounattendXml.FileName, xmlBytes);

        Directory.CreateDirectory(vmFolderPath);
        string path = Path.Combine(vmFolderPath, SeedFileName);
        builder.Build(path);

        return path;
    }
}

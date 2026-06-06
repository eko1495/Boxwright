using System.Text.RegularExpressions;

namespace Boxwright.Core;

/// <summary>
/// Fedora unattended install via an Anaconda kickstart on a <b>netinst</b> ISO (ADR-0017). Extracts the
/// netinst's <c>images/pxeboot/vmlinuz</c> + <c>initrd.img</c>, injects a generated <c>ks.cfg</c> into
/// the initrd (<see cref="InitrdFileInjector"/>), and boots with <c>inst.ks=file:/ks.cfg</c> so Anaconda
/// runs fully non-interactively. Because we boot the kernel/initrd directly (not the ISO's own loader),
/// the kernel command line also needs <c>inst.stage2=</c> pointing at the netinst medium; we reuse the
/// label from the ISO's own <c>grub.cfg</c> (authoritative), falling back to its volume label. The
/// kickstart lives in the initrd, so no extra seed disk is attached. Pure managed (DiscUtils +
/// gzip/cpio) — works the same on Windows, macOS, and Linux. A Fedora <b>Live</b> ISO has no
/// <c>images/pxeboot</c> installer and cannot kickstart — this requires the netinst.
/// </summary>
public sealed partial class FedoraKickstartInstaller : IUnattendedInstaller
{
    /// <summary>The extracted kernel file name written into the VM folder.</summary>
    public const string KernelFileName = "vmlinuz";

    /// <summary>The extracted initrd file name (with the kickstart appended) written into the VM folder.</summary>
    public const string InitrdFileName = "initrd";

    private const string KernelIsoPath = @"images\pxeboot\vmlinuz";
    private const string InitrdIsoPath = @"images\pxeboot\initrd.img";
    private const string GrubCfgIsoPath = @"EFI\BOOT\grub.cfg";

    /// <inheritdoc />
    public string OsFamily => "fedora";

    /// <inheritdoc />
    public UnattendedInstallPlan Prepare(string isoPath, string vmFolderPath, UnattendedAnswers answers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(isoPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(vmFolderPath);
        ArgumentNullException.ThrowIfNull(answers);

        Directory.CreateDirectory(vmFolderPath);

        using IsoMedia iso = IsoMedia.Open(isoPath);

        if (!iso.FileExists(KernelIsoPath))
        {
            throw new InstallMediaException(
                $"'{Path.GetFileName(isoPath)}' has no installer kernel at /images/pxeboot/vmlinuz — it may not be a Fedora netinst ISO (a Live ISO can't kickstart).");
        }

        if (!iso.FileExists(InitrdIsoPath))
        {
            throw new InstallMediaException($"'{Path.GetFileName(isoPath)}' has no installer initrd at /images/pxeboot/initrd.img.");
        }

        iso.CopyFile(KernelIsoPath, Path.Combine(vmFolderPath, KernelFileName));
        string initrdDest = Path.Combine(vmFolderPath, InitrdFileName);
        iso.CopyFile(InitrdIsoPath, initrdDest);

        InitrdFileInjector.Append(initrdDest, FedoraKickstart.FileName, FedoraKickstart.Build(answers));

        string grubCfg = iso.FileExists(GrubCfgIsoPath) ? iso.ReadText(GrubCfgIsoPath) : string.Empty;

        return new UnattendedInstallPlan
        {
            Boot = new InstallBootConfig
            {
                KernelFile = KernelFileName,
                InitrdFile = InitrdFileName,
                Append = BuildAppend(grubCfg, iso.VolumeLabel),
            },
            SeedDisks = [],
        };
    }

    /// <summary>
    /// Composes the kernel command line: <c>inst.ks=file:/ks.cfg</c> + the netinst's own
    /// <c>inst.stage2=</c> parsed from <paramref name="grubCfgContent"/> (authoritative label), or a
    /// fallback <c>inst.stage2=hd:LABEL=&lt;volume label&gt;</c> when the grub.cfg has none. <c>inst.text</c>
    /// keeps Anaconda on the non-graphical installer.
    /// </summary>
    public static string BuildAppend(string grubCfgContent, string isoVolumeLabel)
    {
        Match match = Stage2Line().Match(grubCfgContent ?? string.Empty);
        string stage2 = match.Success
            ? match.Value
            : $"inst.stage2=hd:LABEL={EscapeLabel(isoVolumeLabel ?? string.Empty)}";

        return $"inst.ks=file:/{FedoraKickstart.FileName} {stage2} inst.text";
    }

    // dracut matches a filesystem LABEL with spaces escaped as \x20 on the kernel command line.
    private static string EscapeLabel(string label) => label.Replace(" ", @"\x20", StringComparison.Ordinal);

    [GeneratedRegex(@"inst\.stage2=\S+", RegexOptions.IgnoreCase)]
    private static partial Regex Stage2Line();
}

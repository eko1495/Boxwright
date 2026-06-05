namespace Boxwright.Core;

/// <summary>
/// Debian unattended install via debian-installer preseed (ADR-0016). Extracts the ISO's <b>text</b>
/// installer kernel/initrd (<c>install.amd/vmlinuz</c> + <c>install.amd/initrd.gz</c> — the gtk
/// installer can't read an initrd preseed), injects a generated <c>preseed.cfg</c> into the initrd
/// (<see cref="InitrdPreseedInjector"/>), and boots with <c>auto=true priority=critical</c> so d-i
/// runs fully non-interactively. The preseed lives inside the initrd, so no extra seed disk is
/// attached. Pure managed (DiscUtils + gzip/cpio) — works the same on Windows, macOS, and Linux.
/// </summary>
public sealed class DebianPreseedInstaller : IUnattendedInstaller
{
    /// <summary>The extracted kernel file name written into the VM folder.</summary>
    public const string KernelFileName = "vmlinuz";

    /// <summary>The extracted initrd file name (with the preseed appended) written into the VM folder.</summary>
    public const string InitrdFileName = "initrd";

    // d-i reads /preseed.cfg from the initrd automatically; `auto`/`priority=critical` suppress the
    // early prompts that fire before the preseed is loaded.
    private const string KernelArgs = "auto=true priority=critical";

    // The text installer (NOT install.amd/gtk) — required for an initrd preseed.
    private const string KernelIsoPath = @"install.amd\vmlinuz";
    private static readonly string[] InitrdIsoPaths = [@"install.amd\initrd.gz"];

    /// <inheritdoc />
    public string OsFamily => "debian";

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
                $"'{Path.GetFileName(isoPath)}' has no installer kernel at /install.amd/vmlinuz — it may not be a Debian installer ISO.");
        }

        iso.CopyFile(KernelIsoPath, Path.Combine(vmFolderPath, KernelFileName));

        string? initrdSource = Array.Find(InitrdIsoPaths, iso.FileExists)
            ?? throw new InstallMediaException($"'{Path.GetFileName(isoPath)}' has no installer initrd under /install.amd.");
        string initrdDest = Path.Combine(vmFolderPath, InitrdFileName);
        iso.CopyFile(initrdSource, initrdDest);

        InitrdPreseedInjector.Append(initrdDest, DebianPreseed.FileName, DebianPreseed.Build(answers));

        return new UnattendedInstallPlan
        {
            Boot = new InstallBootConfig { KernelFile = KernelFileName, InitrdFile = InitrdFileName, Append = KernelArgs },
            SeedDisks = [],
        };
    }
}

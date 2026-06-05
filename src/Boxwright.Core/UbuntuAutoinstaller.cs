namespace Boxwright.Core;

/// <summary>
/// Ubuntu unattended install (subiquity autoinstall): bakes a cloud-init <c>CIDATA</c> seed via
/// <see cref="ISeedGenerator"/> and extracts the ISO's casper kernel/initrd via
/// <see cref="IInstallMediaExtractor"/>, booting with <c>autoinstall ds=nocloud</c> so the installer
/// runs hands-free (ADR-0013 Phase B). The seed is attached as a tiny raw virtio disk.
/// </summary>
public sealed class UbuntuAutoinstaller : IUnattendedInstaller
{
    /// <summary>The kernel command line that makes the Ubuntu installer run fully non-interactively.</summary>
    private const string SeedArgs = "autoinstall ds=nocloud";

    private readonly ISeedGenerator _seedGenerator;
    private readonly IInstallMediaExtractor _extractor;

    public UbuntuAutoinstaller(ISeedGenerator seedGenerator, IInstallMediaExtractor extractor)
    {
        ArgumentNullException.ThrowIfNull(seedGenerator);
        ArgumentNullException.ThrowIfNull(extractor);
        _seedGenerator = seedGenerator;
        _extractor = extractor;
    }

    /// <inheritdoc />
    public string OsFamily => "ubuntu";

    /// <inheritdoc />
    public UnattendedInstallPlan Prepare(string isoPath, string vmFolderPath, UnattendedAnswers answers)
    {
        ArgumentNullException.ThrowIfNull(answers);

        _seedGenerator.Generate(answers, vmFolderPath);
        return new UnattendedInstallPlan
        {
            Boot = _extractor.Extract(isoPath, vmFolderPath, SeedArgs),
            SeedDisks = [new DiskConfig { File = CloudInitSeedGenerator.SeedFileName, Format = "raw", Interface = "virtio" }],
        };
    }
}

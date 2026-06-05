using Boxwright.Core;
using Xunit;

namespace Boxwright.Core.Tests;

public sealed class UbuntuAutoinstallerTests
{
    [Fact]
    public void OsFamily_IsUbuntu() => Assert.Equal("ubuntu", new UbuntuAutoinstaller(new StubSeed(), new StubExtractor()).OsFamily);

    [Fact]
    public void Prepare_GeneratesTheSeed_ExtractsTheKernel_AndAttachesTheCidataDisk()
    {
        var seed = new StubSeed();
        var extractor = new StubExtractor();
        var installer = new UbuntuAutoinstaller(seed, extractor);
        var answers = new UnattendedAnswers { Username = "alice" };

        UnattendedInstallPlan plan = installer.Prepare("/iso/ubuntu.iso", "/vm/folder", answers);

        // The cloud-init seed was generated (default InstallerAutoinstall profile) into the VM folder.
        (UnattendedAnswers Answers, string Folder, SeedProfile Profile) seedCall = Assert.Single(seed.Calls);
        Assert.Equal("alice", seedCall.Answers.Username);
        Assert.Equal("/vm/folder", seedCall.Folder);
        Assert.Equal(SeedProfile.InstallerAutoinstall, seedCall.Profile);

        // The kernel was extracted with the `autoinstall` seed args, and its InstallBoot is the plan's boot.
        (string Iso, string Folder, string SeedArgs) extractCall = Assert.Single(extractor.Calls);
        Assert.Equal("/iso/ubuntu.iso", extractCall.Iso);
        Assert.Contains("autoinstall", extractCall.SeedArgs, StringComparison.Ordinal);
        Assert.Equal("vmlinuz", plan.Boot.KernelFile);

        // The CIDATA seed is attached as a raw virtio disk.
        DiskConfig disk = Assert.Single(plan.SeedDisks);
        Assert.Equal(CloudInitSeedGenerator.SeedFileName, disk.File);
        Assert.Equal("raw", disk.Format);
    }

    private sealed class StubSeed : ISeedGenerator
    {
        public List<(UnattendedAnswers Answers, string Folder, SeedProfile Profile)> Calls { get; } = [];

        public string Generate(UnattendedAnswers answers, string vmFolderPath, SeedProfile profile = SeedProfile.InstallerAutoinstall)
        {
            Calls.Add((answers, vmFolderPath, profile));
            return Path.Combine(vmFolderPath, CloudInitSeedGenerator.SeedFileName);
        }
    }

    private sealed class StubExtractor : IInstallMediaExtractor
    {
        public List<(string Iso, string Folder, string SeedArgs)> Calls { get; } = [];

        public InstallBootConfig Extract(string isoPath, string vmFolderPath, string seedArgs)
        {
            Calls.Add((isoPath, vmFolderPath, seedArgs));
            return new InstallBootConfig { KernelFile = "vmlinuz", InitrdFile = "initrd", Append = seedArgs };
        }
    }
}

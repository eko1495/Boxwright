using Boxwright.App.ViewModels;
using Boxwright.Core;
using Xunit;

namespace Boxwright.App.Tests;

// CatalogViewModel now owns only presentation (prefill, validation, progress, cancellation) and
// delegates the actual create to ICatalogVmInstaller. These tests assert that delegation and the
// view-model behavior; the download/disk/seed orchestration is covered by CatalogVmInstallerTests in Core.
public sealed class CatalogViewModelTests
{
    private static OsCatalogEntry SampleEntry() => new()
    {
        Id = "test-os",
        Name = "Test OS",
        Version = "1.0",
        Arch = "x86_64",
        IsoUrl = new Uri("https://example.com/test.iso"),
        Sha256 = new string('a', 64),
        SizeBytes = 1000,
        SourceName = "Example",
        Recommended = new OsRecommendedSpec { MemoryMiB = 4096, CpuCores = 4, DiskGiB = 30, Firmware = "uefi" },
    };

    private static OsCatalogEntry CloudImageEntry() => new()
    {
        Id = "ubuntu-cloud",
        Name = "Ubuntu (cloud)",
        Version = "24.04",
        Arch = "x86_64",
        ImageKind = OsCatalogEntry.ImageKindCloudImage,
        OsFamily = "ubuntu",
        SupportsAutoinstall = true,
        IsoUrl = new Uri("https://example.com/ubuntu-cloud.img"),
        Sha256 = new string('b', 64),
        SizeBytes = 1000,
        SourceName = "Canonical",
        Recommended = new OsRecommendedSpec { MemoryMiB = 2048, CpuCores = 2, DiskGiB = 20, Firmware = "uefi" },
    };

    private static (CatalogViewModel Vm, FakeCatalogVmInstaller Installer) Build(
        Func<string, bool>? isNameTaken = null,
        Exception? installFails = null)
    {
        var source = new FakeOsCatalogSource();
        source.Entries.Add(SampleEntry());
        var installer = new FakeCatalogVmInstaller { FailWith = installFails };
        var vm = new CatalogViewModel(source, installer, new ImmediateUiDispatcher(), isNameTaken ?? (_ => false));
        return (vm, installer);
    }

    [Fact]
    public async Task LoadEntries_PopulatesGallery()
    {
        (CatalogViewModel vm, _) = Build();

        await vm.LoadEntriesCommand.ExecuteAsync(null);

        Assert.Single(vm.Entries);
    }

    [Fact]
    public void SelectingEntry_PrefillsRecommendedSpecs()
    {
        (CatalogViewModel vm, _) = Build();

        vm.SelectedEntry = SampleEntry();

        Assert.Equal("Test OS", vm.Name);
        Assert.Equal(4096, vm.MemoryMiB);
        Assert.Equal(4, vm.CpuCores);
        Assert.Equal(30, vm.DiskSizeGiB);
        Assert.Equal("uefi", vm.Firmware);
        Assert.True(vm.HasSelection);
        Assert.True(vm.GetItCommand.CanExecute(null));
    }

    [Fact]
    public async Task GetIt_HappyPath_DelegatesWithRecommendedOptions_AndRaisesCreated()
    {
        (CatalogViewModel vm, FakeCatalogVmInstaller installer) = Build();
        vm.SelectedEntry = SampleEntry();
        Vm? created = null;
        vm.Created += (_, v) => created = v;

        await vm.GetItCommand.ExecuteAsync(null);

        Assert.NotNull(created);
        Assert.False(vm.IsDownloading);
        Assert.Null(vm.ErrorMessage);

        Assert.Equal("test-os", installer.Entry!.Id);
        CatalogInstallOptions options = installer.Options!;
        Assert.Equal("Test OS", options.Name);
        Assert.Equal(4096, options.MemoryMiB);
        Assert.Equal(4, options.CpuCores);
        Assert.Equal(30, options.DiskSizeGiB);
        Assert.Equal("uefi", options.Firmware);
        Assert.False(options.Unattended); // SampleEntry doesn't support autoinstall
        Assert.Null(options.Answers);
    }

    [Fact]
    public async Task GetIt_UnattendedOptInOffByDefault_DelegatesAttended()
    {
        (CatalogViewModel vm, FakeCatalogVmInstaller installer) = Build();
        vm.SelectedEntry = SampleEntry() with { SupportsAutoinstall = true, OsFamily = "ubuntu" };

        Assert.False(vm.UnattendedEnabled); // opt-in: off until the user ticks it

        await vm.GetItCommand.ExecuteAsync(null);

        Assert.False(installer.Options!.Unattended);
        Assert.Null(installer.Options.Answers);
    }

    [Fact]
    public async Task GetIt_UnattendedOptIn_PassesUnattendedOptionsWithAnswers()
    {
        (CatalogViewModel vm, FakeCatalogVmInstaller installer) = Build();
        vm.SelectedEntry = SampleEntry() with { SupportsAutoinstall = true, OsFamily = "ubuntu" };
        vm.UnattendedEnabled = true;
        vm.UnattendedUsername = "alice";
        vm.UnattendedPassword = "secret";

        await vm.GetItCommand.ExecuteAsync(null);

        Assert.True(installer.Options!.Unattended);
        Assert.Equal("alice", installer.Options.Answers!.Username);
        Assert.Equal("secret", installer.Options.Answers.Password);
    }

    [Fact]
    public async Task GetIt_CloudImage_AlwaysSeeds_ButLeavesUnattendedFlagOff()
    {
        (CatalogViewModel vm, FakeCatalogVmInstaller installer) = Build();
        vm.SelectedEntry = CloudImageEntry();
        vm.UnattendedUsername = "alice";
        vm.UnattendedPassword = "secret";

        await vm.GetItCommand.ExecuteAsync(null);

        Assert.Equal("ubuntu-cloud", installer.Entry!.Id);
        Assert.False(installer.Options!.Unattended);   // Core seeds a cloud image regardless of this flag
        Assert.NotNull(installer.Options.Answers);      // credentials are still supplied
        Assert.Equal("alice", installer.Options.Answers!.Username);
    }

    [Fact]
    public async Task GetIt_Cancelled_CreatesNoVmAndNoError()
    {
        (CatalogViewModel vm, _) = Build(installFails: new OperationCanceledException());
        vm.SelectedEntry = SampleEntry();
        Vm? created = null;
        vm.Created += (_, v) => created = v;

        await vm.GetItCommand.ExecuteAsync(null);

        Assert.Null(created);
        Assert.Null(vm.ErrorMessage);
        Assert.False(vm.IsDownloading);
    }

    [Fact]
    public async Task GetIt_DownloadFailure_ShowsErrorAndCreatesNoVm()
    {
        (CatalogViewModel vm, _) = Build(installFails: new DownloadException("network down"));
        vm.SelectedEntry = SampleEntry();
        Vm? created = null;
        vm.Created += (_, v) => created = v;

        await vm.GetItCommand.ExecuteAsync(null);

        Assert.Null(created);
        Assert.Equal("network down", vm.ErrorMessage);
        Assert.False(vm.IsDownloading);
    }

    [Fact]
    public async Task GetIt_DiskFailure_ShowsErrorAndCreatesNoVm()
    {
        (CatalogViewModel vm, _) = Build(installFails: new DiskException("out of space"));
        vm.SelectedEntry = SampleEntry();
        Vm? created = null;
        vm.Created += (_, v) => created = v;

        await vm.GetItCommand.ExecuteAsync(null);

        Assert.Null(created);
        Assert.NotNull(vm.ErrorMessage);
        Assert.False(vm.IsDownloading);
    }

    [Fact]
    public void CloudImage_RequiresCredentials_AndHidesAutoinstallOptIn()
    {
        (CatalogViewModel vm, _) = Build();

        vm.SelectedEntry = CloudImageEntry();

        Assert.True(vm.IsCloudImage);
        Assert.False(vm.ShowAutoinstallOptIn); // no experimental opt-in for a pre-installed image
        Assert.True(vm.HasValidationError);    // password is required (the image has no default login)
        Assert.False(vm.GetItCommand.CanExecute(null));

        vm.UnattendedPassword = "secret";

        Assert.False(vm.HasValidationError);
        Assert.True(vm.GetItCommand.CanExecute(null));
    }

    [Fact]
    public void NameCollision_BlocksGetIt()
    {
        (CatalogViewModel vm, _) = Build(isNameTaken: _ => true);

        vm.SelectedEntry = SampleEntry();

        Assert.True(vm.HasValidationError);
        Assert.False(vm.GetItCommand.CanExecute(null));
    }
}

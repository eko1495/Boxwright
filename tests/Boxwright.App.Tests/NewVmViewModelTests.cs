using Boxwright.App.ViewModels;
using Boxwright.Core;
using Xunit;

namespace Boxwright.App.Tests;

/// <summary>
/// Exercises <see cref="NewVmViewModel"/> against a real <see cref="VmRepository"/>
/// over a temp dir and a fake disk service — validation, the create flow, and rollback.
/// </summary>
public sealed class NewVmViewModelTests : IDisposable
{
    private readonly string _root;
    private readonly VmRepository _repository;

    public NewVmViewModelTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "boxwright-newvm-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _repository = new VmRepository(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private NewVmViewModel NewForm(
        IDiskService disk,
        Func<string, bool>? isNameTaken = null,
        FakeFilePicker? filePicker = null,
        FakeAutounattendSeedGenerator? autounattend = null,
        FakeIsoDownloader? downloader = null) =>
        new(_repository, disk, filePicker ?? new FakeFilePicker(),
            autounattend ?? new FakeAutounattendSeedGenerator(),
            downloader ?? new FakeIsoDownloader(), new ImmediateUiDispatcher(), isNameTaken ?? (_ => false));

    [Fact]
    public void Defaults_AreSaneAndValid()
    {
        var form = NewForm(new FakeDiskService());
        form.Name = "Ubuntu";

        Assert.Equal(2048, form.MemoryMiB);
        Assert.Equal(2, form.CpuCores);
        Assert.Equal(20, form.DiskSizeGiB);
        Assert.Equal("bios", form.Firmware);
        Assert.Equal("linux", form.OsType);
        Assert.Null(form.ValidationError);
        Assert.True(form.CreateCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("", 2048, 2, 20)]
    [InlineData("ok", 0, 2, 20)]
    [InlineData("ok", 2048, 0, 20)]
    [InlineData("ok", 2048, 2, 0)]
    public void Validation_RejectsBadInput(string name, int memory, int cores, int disk)
    {
        var form = NewForm(new FakeDiskService());
        form.Name = name;
        form.MemoryMiB = memory;
        form.CpuCores = cores;
        form.DiskSizeGiB = disk;

        Assert.NotNull(form.ValidationError);
        Assert.False(form.CreateCommand.CanExecute(null));
    }

    [Fact]
    public void Validation_RejectsDuplicateName()
    {
        var form = NewForm(new FakeDiskService(), isNameTaken: n => n == "Taken");
        form.Name = "Taken";

        Assert.NotNull(form.ValidationError);
        Assert.False(form.CreateCommand.CanExecute(null));
    }

    [Fact]
    public async Task Create_WritesConfigAndDisk_AndRaisesCreated()
    {
        var disk = new FakeDiskService();
        var form = NewForm(disk);
        form.Name = "  Ubuntu  ";
        form.MemoryMiB = 4096;
        form.CpuCores = 4;
        form.DiskSizeGiB = 30;
        form.Firmware = "uefi";
        form.OsType = "windows";
        Vm? created = null;
        form.Created += (_, vm) => created = vm;

        await form.CreateCommand.ExecuteAsync(null);

        Assert.NotNull(created);
        Assert.Equal("Ubuntu", created!.Config.Name);
        Assert.Equal(4096, created.Config.MemoryMiB);
        Assert.Equal(4, created.Config.Cpu.Cores);
        Assert.Equal("uefi", created.Config.Firmware);
        Assert.Equal("windows", created.Config.OsType);
        DiskConfig diskCfg = Assert.Single(created.Config.Disks);
        Assert.Equal(NewVmViewModel.DiskFileName, diskCfg.File);

        Assert.True(File.Exists(created.ConfigPath));
        (string path, long size, string format) = Assert.Single(disk.Created);
        Assert.Equal(Path.Combine(created.FolderPath, NewVmViewModel.DiskFileName), path);
        Assert.Equal(30L * 1024 * 1024 * 1024, size);
        Assert.Equal("qcow2", format);
    }

    [Fact]
    public async Task Create_WhenDiskFails_RollsBackTheVm_AndReportsError()
    {
        var disk = new FakeDiskService { FailWith = new DiskException("qemu-img boom") };
        var form = NewForm(disk);
        form.Name = "DoomedVm";
        bool createdRaised = false;
        form.Created += (_, _) => createdRaised = true;

        await form.CreateCommand.ExecuteAsync(null);

        Assert.False(createdRaised);
        Assert.True(form.HasErrorMessage);
        Assert.Contains("qemu-img boom", form.ErrorMessage, StringComparison.Ordinal);
        Assert.Empty(await _repository.ListAsync());
    }

    [Fact]
    public void SelectingWindows_DefaultsFirmwareToUefi()
    {
        var form = NewForm(new FakeDiskService());

        form.OsType = "windows";

        Assert.True(form.IsWindows);
        Assert.Equal("uefi", form.Firmware); // Windows 11 needs UEFI
    }

    [Fact]
    public async Task PickWindowsIso_SetsThePath()
    {
        var picker = new FakeFilePicker { IsoToReturn = @"C:\isos\win11.iso" };
        var form = NewForm(new FakeDiskService(), filePicker: picker);

        await form.PickWindowsIsoCommand.ExecuteAsync(null);

        Assert.Equal(@"C:\isos\win11.iso", form.WindowsIsoPath);
        Assert.True(form.HasWindowsIso);
    }

    [Fact]
    public void WindowsUnattended_RequiresIsoAndCredentials()
    {
        var form = NewForm(new FakeDiskService());
        form.Name = "Win11";
        form.OsType = "windows";
        form.WindowsUnattended = true;

        Assert.True(form.HasValidationError); // no ISO yet
        form.WindowsIsoPath = @"C:\isos\win11.iso";
        Assert.True(form.HasValidationError); // no password yet
        form.UnattendedPassword = "hunter2";
        Assert.False(form.HasValidationError);
        Assert.True(form.CreateCommand.CanExecute(null));
    }

    [Fact]
    public async Task Create_WindowsUnattended_GeneratesSeedAndAttachesBothCds()
    {
        var disk = new FakeDiskService();
        var autounattend = new FakeAutounattendSeedGenerator();
        var form = NewForm(disk, autounattend: autounattend);
        form.Name = "Win11";
        form.OsType = "windows";          // also defaults firmware to uefi
        form.WindowsUnattended = true;
        form.WindowsIsoPath = @"C:\isos\win11.iso";
        form.UnattendedUsername = "alice";
        form.UnattendedPassword = "hunter2";
        Vm? created = null;
        form.Created += (_, vm) => created = vm;

        await form.CreateCommand.ExecuteAsync(null);

        Assert.NotNull(created);
        Assert.Equal("windows", created!.Config.OsType);
        Assert.Equal("uefi", created.Config.Firmware);

        DiskConfig diskCfg = Assert.Single(created.Config.Disks);
        Assert.Equal("sata", diskCfg.Interface);             // in-box AHCI, not virtio
        Assert.Equal("e1000e", created.Config.Network.Model); // in-box NIC
        Assert.Equal("cd", created.Config.Boot.Order);        // disk-first, then installer CD
        Assert.True(created.Config.WindowsInstallInProgress); // drives the boot-CD keypress + graduate (ADR-0015)

        // Both CDs attached: the Windows ISO and the generated autounattend ISO.
        Assert.Equal(2, created.Config.RemovableMedia.Count);
        Assert.Contains(created.Config.RemovableMedia, m => m.File == @"C:\isos\win11.iso");
        Assert.Contains(created.Config.RemovableMedia, m => m.File == AutounattendSeedGenerator.SeedFileName);

        // The seed was generated into the VM folder with the entered answers + UEFI layout.
        (UnattendedAnswers answers, WindowsInstallOptions _, bool uefi, string folder) = Assert.Single(autounattend.Calls);
        Assert.Equal("alice", answers.Username);
        Assert.True(uefi);
        Assert.Equal(created.FolderPath, folder);

        // The SATA disk was created.
        Assert.Single(disk.Created);
    }

    [Fact]
    public async Task Create_WindowsUnattendedVirtio_DownloadsDrivers_AttachesIso_UsesVirtioDevices()
    {
        var downloader = new FakeIsoDownloader();
        var autounattend = new FakeAutounattendSeedGenerator();
        var form = NewForm(new FakeDiskService(), autounattend: autounattend, downloader: downloader);
        form.Name = "Win11v";
        form.OsType = "windows";
        form.WindowsUnattended = true;
        form.WindowsIsoPath = @"C:\isos\win11.iso";
        form.UnattendedUsername = "alice";
        form.UnattendedPassword = "hunter2";
        form.UseVirtio = true;
        Vm? created = null;
        form.Created += (_, vm) => created = vm;

        await form.CreateCommand.ExecuteAsync(null);

        Assert.NotNull(created);
        Assert.Contains(downloader.Requested, e => e.Id.StartsWith("virtio-win", StringComparison.Ordinal)); // pinned ISO fetched
        Assert.Equal("virtio", Assert.Single(created!.Config.Disks).Interface);   // virtio-blk disk
        Assert.Equal("virtio-net", created.Config.Network.Model);                 // virtio NIC
        // Three CDs: Windows ISO, virtio-win ISO, autounattend seed.
        Assert.Equal(3, created.Config.RemovableMedia.Count);
        Assert.Contains(created.Config.RemovableMedia, m => m.File == downloader.ReturnPath);
        Assert.True(autounattend.Calls.Single().Options.UseVirtio); // driver injection on
    }

    [Fact]
    public async Task Create_WindowsUnattendedVirtio_WithByoIso_SkipsTheDownload()
    {
        var downloader = new FakeIsoDownloader();
        var form = NewForm(new FakeDiskService(), downloader: downloader);
        form.Name = "Win11byo";
        form.OsType = "windows";
        form.WindowsUnattended = true;
        form.WindowsIsoPath = @"C:\isos\win11.iso";
        form.UnattendedUsername = "alice";
        form.UnattendedPassword = "hunter2";
        form.UseVirtio = true;
        form.VirtioWinIsoPath = @"C:\isos\virtio-win.iso";
        Vm? created = null;
        form.Created += (_, vm) => created = vm;

        await form.CreateCommand.ExecuteAsync(null);

        Assert.NotNull(created);
        Assert.Empty(downloader.Requested); // bring-your-own — no download
        Assert.Contains(created!.Config.RemovableMedia, m => m.File == @"C:\isos\virtio-win.iso");
        Assert.Equal("virtio", Assert.Single(created.Config.Disks).Interface);
    }

    [Fact]
    public async Task Create_WindowsUnattended_WhenSeedFails_RollsBackTheVm()
    {
        var autounattend = new FakeAutounattendSeedGenerator { FailWith = new IOException("no space") };
        var form = NewForm(new FakeDiskService(), autounattend: autounattend);
        form.Name = "Win11";
        form.OsType = "windows";
        form.WindowsUnattended = true;
        form.WindowsIsoPath = @"C:\isos\win11.iso";
        form.UnattendedPassword = "hunter2";
        bool createdRaised = false;
        form.Created += (_, _) => createdRaised = true;

        await form.CreateCommand.ExecuteAsync(null);

        Assert.False(createdRaised);
        Assert.True(form.HasErrorMessage);
        Assert.Empty(await _repository.ListAsync()); // the half-created VM was deleted
    }

    [Fact]
    public void Cancel_RaisesCancelled()
    {
        var form = NewForm(new FakeDiskService());
        bool cancelled = false;
        form.Cancelled += (_, _) => cancelled = true;

        form.CancelCommand.Execute(null);

        Assert.True(cancelled);
    }
}

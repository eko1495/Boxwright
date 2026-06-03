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

    private NewVmViewModel NewForm(IDiskService disk, Func<string, bool>? isNameTaken = null) =>
        new(_repository, disk, isNameTaken ?? (_ => false));

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
    public void Cancel_RaisesCancelled()
    {
        var form = NewForm(new FakeDiskService());
        bool cancelled = false;
        form.Cancelled += (_, _) => cancelled = true;

        form.CancelCommand.Execute(null);

        Assert.True(cancelled);
    }
}

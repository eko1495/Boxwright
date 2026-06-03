using Boxwright.App.ViewModels;
using Boxwright.Core;
using Xunit;

namespace Boxwright.App.Tests;

/// <summary>
/// Exercises <see cref="VmSettingsViewModel"/> against a real <see cref="VmRepository"/>
/// over a temp dir: field init, validation, persistence, and preservation of un-edited config.
/// </summary>
public sealed class VmSettingsViewModelTests : IDisposable
{
    private readonly string _root;
    private readonly VmRepository _repository;

    public VmSettingsViewModelTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "boxwright-settings-tests", Guid.NewGuid().ToString("N"));
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

    private Task<Vm> SeedAsync() => _repository.CreateAsync(new VmConfig
    {
        Name = "Original",
        MemoryMiB = 2048,
        Cpu = new CpuConfig { Sockets = 1, Cores = 2, Threads = 1 },
        Firmware = "bios",
        Disks = [new DiskConfig { File = "disk.qcow2", Format = "qcow2", Interface = "virtio" }],
        RemovableMedia = [new RemovableMediaConfig { Type = "cdrom", File = "x.iso", Attached = true }],
    });

    private VmSettingsViewModel NewForm(Vm vm, Func<string, bool>? isNameTakenByOther = null, bool isRunning = false) =>
        new(vm, _repository, isNameTakenByOther ?? (_ => false), isRunning);

    [Fact]
    public async Task Init_PopulatesFieldsFromConfig()
    {
        var form = NewForm(await SeedAsync());

        Assert.Equal("Original", form.Name);
        Assert.Equal(2048, form.MemoryMiB);
        Assert.Equal(2, form.CpuCores);
        Assert.Equal("bios", form.Firmware);
        Assert.Equal("linux", form.OsType);
        Assert.Equal("spice", form.DisplayProtocol);
        Assert.True(form.AudioEnabled); // defaults on (seeded config has no Audio block)
        Assert.Null(form.ValidationError);
        Assert.True(form.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Save_PersistsEdits_PreservesOtherFields_AndRaisesSaved()
    {
        Vm vm = await SeedAsync();
        var form = NewForm(vm);
        form.Name = "Renamed";
        form.MemoryMiB = 8192;
        form.CpuCores = 4;
        form.Firmware = "uefi";
        form.OsType = "windows";
        form.DisplayProtocol = "vnc";
        form.DisplayGl = true;
        form.BootMenu = true;
        form.AudioEnabled = false;
        VmConfig? saved = null;
        form.Saved += (_, cfg) => saved = cfg;

        await form.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(saved);
        VmConfig reloaded = (await _repository.ListAsync()).Single().Config;
        Assert.Equal("Renamed", reloaded.Name);
        Assert.Equal(8192, reloaded.MemoryMiB);
        Assert.Equal(4, reloaded.Cpu.Cores);
        Assert.Equal("uefi", reloaded.Firmware);
        Assert.Equal("windows", reloaded.OsType);
        Assert.Equal("vnc", reloaded.Display.Protocol);
        Assert.False(reloaded.Audio.Enabled);
        Assert.True(reloaded.Display.Gl);
        Assert.True(reloaded.Boot.Menu);

        // Preserved (not exposed by this panel):
        Assert.Equal(vm.Config.Id, reloaded.Id);
        Assert.Equal(1, reloaded.Cpu.Sockets);
        Assert.Single(reloaded.Disks);
        Assert.Single(reloaded.RemovableMedia);
    }

    [Theory]
    [InlineData("", 2048, 2)]
    [InlineData("ok", 0, 2)]
    [InlineData("ok", 2048, 0)]
    public async Task Validation_RejectsBadInput(string name, int memory, int cores)
    {
        var form = NewForm(await SeedAsync());
        form.Name = name;
        form.MemoryMiB = memory;
        form.CpuCores = cores;

        Assert.NotNull(form.ValidationError);
        Assert.False(form.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Validation_RejectsNameTakenByAnotherVm()
    {
        var form = NewForm(await SeedAsync(), isNameTakenByOther: n => n == "Taken");
        form.Name = "Taken";

        Assert.NotNull(form.ValidationError);
        Assert.False(form.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Validation_AllowsKeepingTheCurrentName()
    {
        // The predicate excludes self, so leaving the name unchanged stays valid.
        var form = NewForm(await SeedAsync(), isNameTakenByOther: _ => false);

        Assert.Null(form.ValidationError);
        Assert.True(form.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Cancel_RaisesCancelled()
    {
        var form = NewForm(await SeedAsync());
        bool cancelled = false;
        form.Cancelled += (_, _) => cancelled = true;

        form.CancelCommand.Execute(null);

        Assert.True(cancelled);
    }

    [Fact]
    public async Task IsRunning_IsExposedForTheRestartNotice()
    {
        Vm vm = await SeedAsync();

        Assert.True(NewForm(vm, isRunning: true).IsRunning);
        Assert.False(NewForm(vm, isRunning: false).IsRunning);
    }
}

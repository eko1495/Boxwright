using Boxwright.App.ViewModels;
using Boxwright.Core;
using Xunit;

namespace Boxwright.App.Tests;

/// <summary>
/// Exercises <see cref="VmListItemViewModel"/>'s power-control state machine with fake
/// launcher/session doubles — no real QEMU. Delete uses a real repository over a temp dir.
/// </summary>
public sealed class VmListItemViewModelTests : IDisposable
{
    private readonly string _root;
    private readonly VmRepository _repository;
    private readonly ImmediateUiDispatcher _dispatcher = new();
    private readonly FakeFilePicker _filePicker = new();

    public VmListItemViewModelTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "boxwright-item-tests", Guid.NewGuid().ToString("N"));
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

    private VmListItemViewModel NewItem(IVmLauncher launcher, Vm? vm = null) =>
        new(vm ?? new Vm(Path.Combine(_root, "x"), new VmConfig { Id = "x", Name = "Test" }),
            launcher, _repository, _dispatcher, _filePicker);

    [Fact]
    public async Task Start_TransitionsToRunning_AndSurfacesAcceleratorHonesty()
    {
        var item = NewItem(new FakeVmLauncher(new FakeRunningVm { Accelerator = Accelerator.Tcg }));

        await item.StartCommand.ExecuteAsync(null);

        Assert.Equal(VmStatus.Running, item.Status);
        Assert.Contains("TCG", item.StatusMessage, StringComparison.Ordinal);
        Assert.False(item.StartCommand.CanExecute(null));
        Assert.True(item.StopCommand.CanExecute(null));
    }

    [Fact]
    public async Task Start_WhenLaunchFails_StaysStopped_WithActionableMessage()
    {
        var item = NewItem(new ThrowingVmLauncher(new InvalidOperationException("WHPX is not available")));

        await item.StartCommand.ExecuteAsync(null);

        Assert.Equal(VmStatus.Stopped, item.Status);
        Assert.Contains("WHPX is not available", item.StatusMessage, StringComparison.Ordinal);
        Assert.True(item.StartCommand.CanExecute(null));
    }

    [Fact]
    public async Task PauseResumeReset_DriveSessionAndStatus()
    {
        var session = new FakeRunningVm();
        var item = NewItem(new FakeVmLauncher(session));
        await item.StartCommand.ExecuteAsync(null);

        await item.PauseCommand.ExecuteAsync(null);
        Assert.Equal(VmStatus.Paused, item.Status);
        Assert.True(item.ResumeCommand.CanExecute(null));
        Assert.False(item.PauseCommand.CanExecute(null));

        await item.ResumeCommand.ExecuteAsync(null);
        Assert.Equal(VmStatus.Running, item.Status);

        await item.ResetCommand.ExecuteAsync(null);

        Assert.Equal("pause resume reset", string.Join(' ', session.Calls));
    }

    [Fact]
    public async Task Stop_StopsAndDisposesSession_ReturningToStopped()
    {
        var session = new FakeRunningVm();
        var item = NewItem(new FakeVmLauncher(session));
        await item.StartCommand.ExecuteAsync(null);

        await item.StopCommand.ExecuteAsync(null);

        Assert.Equal(VmStatus.Stopped, item.Status);
        Assert.Contains("stop", session.Calls);
        Assert.Contains("dispose", session.Calls);
        Assert.True(item.StartCommand.CanExecute(null));
    }

    [Fact]
    public async Task SessionExit_WhileRunning_ReturnsToStopped()
    {
        var session = new FakeRunningVm();
        var item = NewItem(new FakeVmLauncher(session));
        await item.StartCommand.ExecuteAsync(null);

        session.RaiseExited();

        Assert.Equal(VmStatus.Stopped, item.Status);
        Assert.True(item.StartCommand.CanExecute(null));
    }

    [Fact]
    public async Task Delete_IsTwoStep_RemovesFolder_AndRaisesDeleted()
    {
        Vm vm = await _repository.CreateAsync(new VmConfig { Name = "del-me" });
        var item = NewItem(new FakeVmLauncher(new FakeRunningVm()), vm);
        bool raised = false;
        item.Deleted += (_, _) => raised = true;

        Assert.True(item.DeleteCommand.CanExecute(null));
        item.DeleteCommand.Execute(null);
        Assert.True(item.IsConfirmingDelete);
        Assert.False(raised);
        Assert.True(Directory.Exists(vm.FolderPath));

        await item.ConfirmDeleteCommand.ExecuteAsync(null);

        Assert.True(raised);
        Assert.False(item.IsConfirmingDelete);
        Assert.False(Directory.Exists(vm.FolderPath));
    }

    [Fact]
    public async Task Delete_IsDisabled_WhileRunning()
    {
        var item = NewItem(new FakeVmLauncher(new FakeRunningVm()));
        await item.StartCommand.ExecuteAsync(null);

        Assert.False(item.DeleteCommand.CanExecute(null));
    }

    [Fact]
    public async Task ChooseIso_RecordsCdromMedia_AndSetsCdFirstBoot()
    {
        Vm vm = await _repository.CreateAsync(new VmConfig { Name = "iso-test" });
        var item = NewItem(new FakeVmLauncher(new FakeRunningVm()), vm);
        _filePicker.IsoToReturn = Path.Combine(_root, "ubuntu.iso");

        await item.ChooseIsoCommand.ExecuteAsync(null);

        Assert.True(item.HasIso);
        Assert.Equal(_filePicker.IsoToReturn, item.IsoPath);

        VmConfig reloaded = (await _repository.ListAsync()).Single().Config;
        RemovableMediaConfig media = Assert.Single(reloaded.RemovableMedia);
        Assert.True(media.Attached);
        Assert.Equal("cdrom", media.Type);
        Assert.Equal("dc", reloaded.Boot.Order);
    }

    [Fact]
    public async Task ChooseIso_WhenCancelled_LeavesConfigUnchanged()
    {
        Vm vm = await _repository.CreateAsync(new VmConfig { Name = "iso-cancel" });
        var item = NewItem(new FakeVmLauncher(new FakeRunningVm()), vm);
        _filePicker.IsoToReturn = null;

        await item.ChooseIsoCommand.ExecuteAsync(null);

        Assert.False(item.HasIso);
        Assert.Empty((await _repository.ListAsync()).Single().Config.RemovableMedia);
    }

    [Fact]
    public async Task RemoveIso_DetachesIso_AndBootsFromDisk()
    {
        Vm vm = await _repository.CreateAsync(new VmConfig
        {
            Name = "iso-remove",
            RemovableMedia = [new RemovableMediaConfig { Type = "cdrom", File = "ubuntu.iso", Attached = true }],
            Boot = new BootConfig { Order = "dc" },
        });
        var item = NewItem(new FakeVmLauncher(new FakeRunningVm()), vm);
        Assert.True(item.HasIso);

        await item.RemoveIsoCommand.ExecuteAsync(null);

        Assert.False(item.HasIso);
        VmConfig reloaded = (await _repository.ListAsync()).Single().Config;
        Assert.Empty(reloaded.RemovableMedia);
        Assert.Equal("c", reloaded.Boot.Order);
    }

    [Fact]
    public async Task IsoCommands_AreDisabledWhileRunning()
    {
        var item = NewItem(new FakeVmLauncher(new FakeRunningVm()));
        await item.StartCommand.ExecuteAsync(null);

        Assert.False(item.ChooseIsoCommand.CanExecute(null));
        Assert.False(item.RemoveIsoCommand.CanExecute(null));
    }
}

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
    private readonly FakeDisplayLauncher _display = new();
    private readonly FakeEmbeddedVncDisplay _embeddedVnc = new();
    private readonly FakeLogReader _logReader = new();
    private readonly FakeSnapshotService _snapshots = new();
    private readonly FakeVmCloneService _clone = new();

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
            launcher, _repository, _dispatcher, _filePicker, _display, _embeddedVnc, _logReader, _snapshots, _clone);

    private Vm SnapshottableVm() =>
        new(Path.Combine(_root, "x"), new VmConfig
        {
            Id = "x",
            Name = "Snap",
            Disks = [new DiskConfig { File = "disk.qcow2", Format = "qcow2", Interface = "virtio" }],
        });

    [Fact]
    public async Task TakeSnapshot_CreatesViaService_AndItAppearsInTheList()
    {
        var item = NewItem(new FakeVmLauncher(new FakeRunningVm()), SnapshottableVm());
        item.NewSnapshotName = "before-update";

        await item.TakeSnapshotCommand.ExecuteAsync(null);

        Assert.Contains("create:before-update", _snapshots.Calls);
        Assert.Contains(item.Snapshots, s => s.Name == "before-update");
        Assert.True(item.HasSnapshots);
    }

    [Fact]
    public async Task RestoreSnapshot_AsksForConfirmation_ThenRestoresViaService()
    {
        var item = NewItem(new FakeVmLauncher(new FakeRunningVm()), SnapshottableVm());
        var snapshot = new VmSnapshot { Name = "snap1" };

        item.RestoreSnapshotCommand.Execute(snapshot);
        Assert.True(item.IsConfirmingRestore);
        Assert.Equal(snapshot, item.SnapshotPendingRestore);

        await item.ConfirmRestoreSnapshotCommand.ExecuteAsync(null);

        Assert.False(item.IsConfirmingRestore);
        Assert.Contains("restore:snap1", _snapshots.Calls);
    }

    [Fact]
    public async Task Snapshots_CannotBeManaged_WhileRunning()
    {
        var item = NewItem(new FakeVmLauncher(new FakeRunningVm()), SnapshottableVm());
        Assert.True(item.CanManageSnapshots);

        await item.StartCommand.ExecuteAsync(null);

        Assert.False(item.CanManageSnapshots);
    }

    [Fact]
    public async Task FullClone_InvokesCloneService_AndRaisesClonedWithTheNewVm()
    {
        var item = NewItem(new FakeVmLauncher(new FakeRunningVm()), SnapshottableVm());
        item.CloneName = "my-copy";
        Vm? cloned = null;
        item.Cloned += (_, vm) => cloned = vm;

        await item.FullCloneCommand.ExecuteAsync(null);

        Assert.Contains(("my-copy", CloneMode.Full), _clone.Clones);
        Assert.Equal("my-copy", cloned?.Config.Name);
    }

    [Fact]
    public async Task LinkedClone_WithNoName_DefaultsToSourceNamePlusClone()
    {
        var item = NewItem(new FakeVmLauncher(new FakeRunningVm()), SnapshottableVm()); // Name = "Snap"

        await item.LinkedCloneCommand.ExecuteAsync(null);

        Assert.Contains(("Snap (clone)", CloneMode.Linked), _clone.Clones);
    }

    [Fact]
    public async Task Clone_IsDisabledWhileRunning()
    {
        var item = NewItem(new FakeVmLauncher(new FakeRunningVm()), SnapshottableVm());
        await item.StartCommand.ExecuteAsync(null);

        Assert.False(item.CanClone);
    }

    [Fact]
    public async Task SaveState_SavesViaSession_ThenPowersOff()
    {
        var session = new FakeRunningVm();
        var item = NewItem(new FakeVmLauncher(session), SnapshottableVm());
        await item.StartCommand.ExecuteAsync(null);

        await item.SaveStateCommand.ExecuteAsync(null);

        Assert.Contains("savestate", session.Calls);
        Assert.Contains("forcestop", session.Calls);
        Assert.Equal(VmStatus.Stopped, item.Status);
    }

    [Fact]
    public async Task SaveState_IsUnavailable_UnderWhpx()
    {
        // WHPX can't serialize VM state, so Save state must be gated off (not fail at runtime).
        var item = NewItem(new FakeVmLauncher(new FakeRunningVm { Accelerator = Accelerator.Whpx }), SnapshottableVm());
        await item.StartCommand.ExecuteAsync(null);

        Assert.False(item.CanSaveState);
    }

    [Fact]
    public async Task Start_WithSavedState_ResumesAndConsumesIt()
    {
        var session = new FakeRunningVm();
        var item = NewItem(new FakeVmLauncher(session), SnapshottableVm());
        _snapshots.Snapshots.Add(new VmSnapshot { Name = "boxwright-saved-state" });
        await item.RefreshSnapshotsCommand.ExecuteAsync(null);
        Assert.True(item.HasSavedState);

        await item.StartCommand.ExecuteAsync(null);

        Assert.Contains("loadstate", session.Calls);    // resumed
        Assert.Contains("deletestate", session.Calls);  // and consumed
        Assert.False(item.HasSavedState);
        Assert.Equal(VmStatus.Running, item.Status);
    }

    [Fact]
    public async Task RefreshSnapshots_SurfacesSavedStateSeparately_FromUserSnapshots()
    {
        var item = NewItem(new FakeVmLauncher(new FakeRunningVm()), SnapshottableVm());
        _snapshots.Snapshots.Add(new VmSnapshot { Name = "boxwright-saved-state" });
        _snapshots.Snapshots.Add(new VmSnapshot { Name = "user-snap" });

        await item.RefreshSnapshotsCommand.ExecuteAsync(null);

        Assert.True(item.HasSavedState);
        Assert.DoesNotContain(item.Snapshots, s => s.Name == "boxwright-saved-state");
        Assert.Contains(item.Snapshots, s => s.Name == "user-snap");
    }

    [Fact]
    public async Task DiscardSavedState_DeletesTheReservedSnapshot()
    {
        var item = NewItem(new FakeVmLauncher(new FakeRunningVm()), SnapshottableVm());
        _snapshots.Snapshots.Add(new VmSnapshot { Name = "boxwright-saved-state" });
        await item.RefreshSnapshotsCommand.ExecuteAsync(null);
        Assert.True(item.HasSavedState);

        await item.DiscardSavedStateCommand.ExecuteAsync(null);

        Assert.Contains("delete:boxwright-saved-state", _snapshots.Calls);
        Assert.False(item.HasSavedState);
    }

    [Fact]
    public async Task RefreshGuestIp_PopulatesAddressesFromTheSession()
    {
        var session = new FakeRunningVm();
        session.GuestAddresses.Add("10.0.2.15");
        var item = NewItem(new FakeVmLauncher(session), SnapshottableVm());
        await item.StartCommand.ExecuteAsync(null);

        await item.RefreshGuestIpCommand.ExecuteAsync(null);

        Assert.Equal("10.0.2.15", item.GuestAddresses);
        Assert.True(item.HasGuestAddresses);
    }

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
    public async Task TryAdopt_WithAdoptableSession_ShowsRunningWithoutStarting()
    {
        var adopted = new FakeRunningVm { Accelerator = Accelerator.Whpx };
        var launcher = new FakeVmLauncher(new FakeRunningVm()) { AdoptResult = adopted };
        var item = NewItem(launcher);
        Assert.False(item.IsLive);

        await item.TryAdoptAsync();

        Assert.True(item.IsLive);
        Assert.Equal(VmStatus.Running, item.Status);
        Assert.Contains("Reconnected", item.StatusMessage, StringComparison.Ordinal);
        Assert.True(item.StopCommand.CanExecute(null));
        Assert.Null(launcher.LastVm); // re-adopted, never started
    }

    [Fact]
    public async Task TryAdopt_WithNothingToAdopt_StaysStopped()
    {
        var item = NewItem(new FakeVmLauncher(new FakeRunningVm())); // AdoptResult defaults to null

        await item.TryAdoptAsync();

        Assert.False(item.IsLive);
        Assert.Equal(VmStatus.Stopped, item.Status);
    }

    [Fact]
    public async Task TryAdopt_WhenTheAdoptedSessionExits_ReturnsToStopped()
    {
        var adopted = new FakeRunningVm();
        var item = NewItem(new FakeVmLauncher(new FakeRunningVm()) { AdoptResult = adopted });
        await item.TryAdoptAsync();
        Assert.True(item.IsLive);

        adopted.RaiseExited();

        Assert.Equal(VmStatus.Stopped, item.Status); // adopted sessions tear down on exit like started ones
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
    public async Task ShutDown_GracefullyStopsAndTearsDownSession()
    {
        var session = new FakeRunningVm();
        var item = NewItem(new FakeVmLauncher(session));
        await item.StartCommand.ExecuteAsync(null);
        Assert.True(item.IsLive);

        await item.ShutDownAsync();

        Assert.False(item.IsLive);
        Assert.Equal(VmStatus.Stopped, item.Status);
        Assert.Contains("stop", session.Calls);            // ACPI power-down, not a hard kill
        Assert.DoesNotContain("forcestop", session.Calls);
        Assert.Contains("dispose", session.Calls);
    }

    [Fact]
    public async Task ForceOff_KillsAndTearsDownSession()
    {
        var session = new FakeRunningVm();
        var item = NewItem(new FakeVmLauncher(session));
        await item.StartCommand.ExecuteAsync(null);

        await item.ForceOffAsync();

        Assert.False(item.IsLive);
        Assert.Equal(VmStatus.Stopped, item.Status);
        Assert.Contains("forcestop", session.Calls);
        Assert.Contains("dispose", session.Calls);
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
    public async Task WhileRunning_AttachIsoIsDisabled_ButRemoveIsoIsAllowed()
    {
        var item = NewItem(new FakeVmLauncher(new FakeRunningVm()));
        await item.StartCommand.ExecuteAsync(null);

        Assert.False(item.ChooseIsoCommand.CanExecute(null)); // attaching/swapping is stopped-only
        Assert.True(item.RemoveIsoCommand.CanExecute(null));  // removal works live (QMP eject)
    }

    [Fact]
    public async Task RemoveIso_WhileRunning_EjectsFromGuest_AndPersistsDetach()
    {
        Vm vm = await _repository.CreateAsync(new VmConfig
        {
            Name = "iso-live-eject",
            RemovableMedia = [new RemovableMediaConfig { Type = "cdrom", File = "ubuntu.iso", Attached = true }],
            Boot = new BootConfig { Order = "dc" },
        });
        var session = new FakeRunningVm();
        var item = NewItem(new FakeVmLauncher(session), vm);
        await item.StartCommand.ExecuteAsync(null);
        Assert.True(item.HasIso);

        await item.RemoveIsoCommand.ExecuteAsync(null);

        Assert.Contains("eject", session.Calls); // live QMP eject from the running guest
        Assert.False(item.HasIso);               // and the detach is persisted
        VmConfig reloaded = (await _repository.ListAsync()).Single().Config;
        Assert.Empty(reloaded.RemovableMedia);
        Assert.Equal("c", reloaded.Boot.Order);
    }

    [Fact]
    public async Task OpenDisplay_WhenRunning_LaunchesViewerAgainstSpicePort()
    {
        var item = NewItem(new FakeVmLauncher(new FakeRunningVm { SpicePort = 5905 }));
        await item.StartCommand.ExecuteAsync(null);

        item.OpenDisplayCommand.Execute(null);

        Assert.Equal((5905, "spice"), Assert.Single(_display.Launches));
    }

    [Fact]
    public async Task OpenDisplay_VncSession_OpensTheEmbeddedViewer()
    {
        var item = NewItem(new FakeVmLauncher(new FakeRunningVm { SpicePort = 5950, DisplayProtocol = "vnc" }));
        await item.StartCommand.ExecuteAsync(null);

        item.OpenDisplayCommand.Execute(null);

        (string Title, string Host, int Port) opened = Assert.Single(_embeddedVnc.Opens);
        Assert.Equal(("127.0.0.1", 5950), (opened.Host, opened.Port));
        Assert.Empty(_display.Launches); // VNC renders in-app — it does NOT shell out to remote-viewer
    }

    [Fact]
    public async Task OpenDisplay_WhenViewerMissing_ShowsActionableMessage()
    {
        var item = NewItem(new FakeVmLauncher(new FakeRunningVm()));
        await item.StartCommand.ExecuteAsync(null);
        _display.FailWith = new DisplayException("remote-viewer was not found");

        item.OpenDisplayCommand.Execute(null);

        Assert.Contains("remote-viewer was not found", item.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenDisplay_IsDisabledWhenStopped()
    {
        var item = NewItem(new FakeVmLauncher(new FakeRunningVm()));

        Assert.False(item.OpenDisplayCommand.CanExecute(null));
    }

    [Fact]
    public void ApplyConfig_UpdatesDisplayedConfig()
    {
        var item = NewItem(new FakeVmLauncher(new FakeRunningVm()));
        VmConfig updated = item.Vm.Config with { Name = "Renamed", MemoryMiB = 9000 };

        item.ApplyConfig(updated);

        Assert.Equal("Renamed", item.Name);
        Assert.Contains("9000 MiB", item.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshLog_PopulatesLogContentFromTheVmLogPath()
    {
        var item = NewItem(new FakeVmLauncher(new FakeRunningVm()));
        _logReader.Content = "=== Boxwright launch ===\n--- QEMU output ---\nVNC server running";

        await item.RefreshLogCommand.ExecuteAsync(null);

        Assert.True(item.HasLog);
        Assert.Equal(_logReader.Content, item.LogContent);
        Assert.Equal(item.Vm.LogPath, _logReader.LastPath);
    }

    [Fact]
    public async Task RefreshLog_WhenNeverRun_LeavesEmptyState()
    {
        var item = NewItem(new FakeVmLauncher(new FakeRunningVm()));
        _logReader.Content = null;

        await item.RefreshLogCommand.ExecuteAsync(null);

        Assert.False(item.HasLog);
        Assert.Null(item.LogContent);
    }

    [Fact]
    public async Task Start_AutoRefreshesTheLog()
    {
        var item = NewItem(new FakeVmLauncher(new FakeRunningVm()));
        _logReader.Content = "boot log";

        await item.StartCommand.ExecuteAsync(null);

        Assert.Equal("boot log", item.LogContent);
        Assert.Equal(item.Vm.LogPath, _logReader.LastPath);
    }

    [Fact]
    public async Task RunningVm_PollsMetrics_AndPopulatesTheHistories()
    {
        var session = new FakeRunningVm();
        // Two scripted samples so a rate can be derived (the poll loop differences successive samples).
        session.MetricSamples.Add(new VmMetricsSample(TimeSpan.Zero, 100_000_000, 0, 0));
        session.MetricSamples.Add(new VmMetricsSample(TimeSpan.FromSeconds(0.5), 120_000_000, 1_000_000, 1_000_000));
        var item = NewItem(new FakeVmLauncher(session), SnapshottableVm());

        await item.StartCommand.ExecuteAsync(null);

        await WaitUntilAsync(() => item.HasMetrics && item.CpuHistory.Length > 0, timeoutMs: 8000);
        Assert.NotEmpty(item.CpuHistory);
        Assert.NotEmpty(item.MemoryHistory);
        Assert.NotEmpty(item.DiskHistory);
        Assert.NotNull(item.MemoryText);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && sw.ElapsedMilliseconds < timeoutMs)
        {
            await Task.Delay(50);
        }

        Assert.True(condition(), "condition was not met within the timeout");
    }
}

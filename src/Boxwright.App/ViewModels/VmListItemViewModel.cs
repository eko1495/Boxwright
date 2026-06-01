using System.Collections.ObjectModel;
using Boxwright.App.Services;
using Boxwright.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Boxwright.App.ViewModels;

/// <summary>The UI-facing lifecycle state of a VM.</summary>
public enum VmStatus
{
    /// <summary>Not running.</summary>
    Stopped,

    /// <summary>Launch in progress.</summary>
    Starting,

    /// <summary>Running.</summary>
    Running,

    /// <summary>Running but paused (guest CPUs stopped).</summary>
    Paused,

    /// <summary>Shutdown in progress.</summary>
    Stopping,
}

/// <summary>
/// A stateful row in the VM list: display info, power controls (start/stop/pause/
/// resume/reset/delete) wired to <see cref="IVmLauncher"/> / <see cref="IRunningVm"/>,
/// and installer-ISO attach/remove with boot-order handling. Holds the live session
/// while running, reflects state, and is honest about acceleration (Directive 9).
/// </summary>
public sealed partial class VmListItemViewModel : ObservableObject
{
    private readonly IVmLauncher _launcher;
    private readonly VmRepository _repository;
    private readonly IUiDispatcher _dispatcher;
    private readonly IFilePicker _filePicker;
    private readonly IDisplayLauncher _displayLauncher;
    private readonly ILogReader _logReader;
    private readonly ISnapshotService _snapshotService;
    private IRunningVm? _session;

    public VmListItemViewModel(
        Vm vm,
        IVmLauncher launcher,
        VmRepository repository,
        IUiDispatcher dispatcher,
        IFilePicker filePicker,
        IDisplayLauncher displayLauncher,
        ILogReader logReader,
        ISnapshotService snapshotService)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(filePicker);
        ArgumentNullException.ThrowIfNull(displayLauncher);
        ArgumentNullException.ThrowIfNull(logReader);
        ArgumentNullException.ThrowIfNull(snapshotService);

        Vm = vm;
        _launcher = launcher;
        _repository = repository;
        _dispatcher = dispatcher;
        _filePicker = filePicker;
        _displayLauncher = displayLauncher;
        _logReader = logReader;
        _snapshotService = snapshotService;
    }

    /// <summary>The underlying domain VM (replaced when its config is edited).</summary>
    public Vm Vm { get; private set; }

    /// <summary>Display name, with a fallback when the config has no name.</summary>
    public string Name => string.IsNullOrWhiteSpace(Vm.Config.Name) ? "(unnamed VM)" : Vm.Config.Name;

    /// <summary>One-line spec summary, e.g. <c>x86_64 · 2 vCPU · 2048 MiB</c>.</summary>
    public string Summary
    {
        get
        {
            CpuConfig cpu = Vm.Config.Cpu;
            int vcpus = cpu.Sockets * cpu.Cores * cpu.Threads;
            return $"{Vm.Config.Arch} · {vcpus} vCPU · {Vm.Config.MemoryMiB} MiB";
        }
    }

    /// <summary>Path of the attached installer ISO, or null when none is attached.</summary>
    public string? IsoPath =>
        Vm.Config.RemovableMedia
            .FirstOrDefault(m => string.Equals(m.Type, "cdrom", StringComparison.OrdinalIgnoreCase) && m.Attached)?.File;

    /// <summary>True when an installer ISO is attached.</summary>
    public bool HasIso => !string.IsNullOrEmpty(IsoPath);

    /// <summary>A friendly description of the configured boot order.</summary>
    public string BootSummary => Vm.Config.Boot.Order.StartsWith('d')
        ? "Boots from CD/ISO first, then disk."
        : "Boots from disk.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(CanManageSnapshots))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand), nameof(PauseCommand),
        nameof(ResumeCommand), nameof(ResetCommand), nameof(DeleteCommand),
        nameof(ChooseIsoCommand), nameof(RemoveIsoCommand), nameof(OpenDisplayCommand))]
    private VmStatus _status = VmStatus.Stopped;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isConfirmingDelete;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLog))]
    private string? _logContent;

    /// <summary>True when there is captured log text to show.</summary>
    public bool HasLog => !string.IsNullOrEmpty(LogContent);

    /// <summary>Human-readable status for the UI.</summary>
    public string StatusText => Status switch
    {
        VmStatus.Stopped => "Stopped",
        VmStatus.Starting => "Starting…",
        VmStatus.Running => "Running",
        VmStatus.Paused => "Paused",
        VmStatus.Stopping => "Stopping…",
        _ => string.Empty,
    };

    /// <summary>True when there is a status/honesty message to show.</summary>
    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    /// <summary>Raised after the VM is deleted from disk, so the list can drop this item.</summary>
    public event EventHandler? Deleted;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        Status = VmStatus.Starting;
        StatusMessage = null;
        try
        {
            IRunningVm session = await _launcher.StartAsync(Vm);
            session.Exited += OnSessionExited;
            _session = session;
            Status = VmStatus.Running;
            StatusMessage = DescribeAccelerator(session.Accelerator);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Degrade gracefully (Directive 4): surface an actionable message, don't crash.
            Status = VmStatus.Stopped;
            StatusMessage = $"Couldn't start the VM: {ex.Message}";
        }

        // Surface what QEMU wrote (the launch header + any output/errors) without a manual click.
        await RefreshLogAsync();
    }

    private bool CanStart() => Status == VmStatus.Stopped;

    // A guest needs more than a few seconds to finish an ACPI shutdown; too short a grace
    // turns Stop into a hard kill, which risks corrupting the guest filesystem. 60s is a
    // VirtualBox-like default. The same path serves the Stop button and the app-close prompt.
    private static readonly TimeSpan ShutdownGrace = TimeSpan.FromSeconds(60);

    /// <summary>True while a live QEMU session is attached (the VM is running, paused, or stopping).</summary>
    internal bool IsLive => _session is not null;

    [RelayCommand(CanExecute = nameof(CanControlRunning))]
    private Task StopAsync() => ShutDownAsync();

    /// <summary>
    /// Graceful shutdown: ACPI power-down, then force-stop if the guest overstays the grace
    /// period. Shared by the Stop button and the app-close prompt.
    /// </summary>
    internal async Task ShutDownAsync()
    {
        Status = VmStatus.Stopping;
        try
        {
            if (_session is not null)
            {
                await _session.StopAsync(ShutdownGrace);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _session?.ForceStop();
            StatusMessage = $"Forced stop after an error: {ex.Message}";
        }
        finally
        {
            await TeardownSessionAsync();
            Status = VmStatus.Stopped;
            await RefreshLogAsync();
            await RefreshSnapshotsAsync(); // snapshots are now manageable again
        }
    }

    /// <summary>Immediately force-stops the VM (pulls the plug) and tears down the session — the app-close "Force off" path.</summary>
    internal async Task ForceOffAsync()
    {
        Status = VmStatus.Stopping;
        _session?.ForceStop();
        await TeardownSessionAsync();
        Status = VmStatus.Stopped;
        await RefreshLogAsync();
    }

    private bool CanControlRunning() => Status is VmStatus.Running or VmStatus.Paused;

    [RelayCommand(CanExecute = nameof(CanPause))]
    private async Task PauseAsync()
    {
        if (_session is null)
        {
            return;
        }

        await _session.PauseAsync();
        Status = VmStatus.Paused;
    }

    private bool CanPause() => Status == VmStatus.Running;

    [RelayCommand(CanExecute = nameof(CanResume))]
    private async Task ResumeAsync()
    {
        if (_session is null)
        {
            return;
        }

        await _session.ResumeAsync();
        Status = VmStatus.Running;
    }

    private bool CanResume() => Status == VmStatus.Paused;

    [RelayCommand(CanExecute = nameof(CanControlRunning))]
    private async Task ResetAsync()
    {
        if (_session is not null)
        {
            await _session.ResetAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanControlRunning))]
    private void OpenDisplay()
    {
        if (_session is null)
        {
            return;
        }

        try
        {
            _displayLauncher.Launch(_session.SpicePort, _session.DisplayProtocol);
        }
        catch (DisplayException ex)
        {
            // remote-viewer missing — show CORE-10's actionable message (Directive 4).
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task RefreshLogAsync() => LogContent = await _logReader.ReadAsync(Vm.LogPath);

    // ---- Snapshots (qcow2 internal; stopped-only) ----

    /// <summary>Snapshots of this VM's primary qcow2 disk (loaded on demand).</summary>
    public ObservableCollection<VmSnapshot> Snapshots { get; } = [];

    [ObservableProperty]
    private string? _newSnapshotName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmingRestore))]
    private VmSnapshot? _snapshotPendingRestore;

    /// <summary>True while a snapshot restore awaits confirmation (it discards the current disk state).</summary>
    public bool IsConfirmingRestore => SnapshotPendingRestore is not null;

    /// <summary>True when there are snapshots to show.</summary>
    public bool HasSnapshots => Snapshots.Count > 0;

    /// <summary>Snapshots can only be managed while the VM is stopped and has a qcow2 disk (offline access).</summary>
    public bool CanManageSnapshots => Status == VmStatus.Stopped && PrimaryDiskPath is not null;

    // qcow2 internal snapshots operate on the first qcow2 disk (the common single-disk case).
    private string? PrimaryDiskPath
    {
        get
        {
            DiskConfig? disk = Vm.Config.Disks
                .FirstOrDefault(d => string.Equals(d.Format, "qcow2", StringComparison.OrdinalIgnoreCase));
            return disk is null ? null : Path.Combine(Vm.FolderPath, disk.File);
        }
    }

    [RelayCommand]
    private async Task RefreshSnapshotsAsync()
    {
        Snapshots.Clear();
        if (CanManageSnapshots && PrimaryDiskPath is { } disk)
        {
            try
            {
                foreach (VmSnapshot snapshot in await _snapshotService.ListAsync(disk))
                {
                    Snapshots.Add(snapshot);
                }
            }
            catch (DiskException ex)
            {
                StatusMessage = $"Couldn't list snapshots: {ex.Message}";
            }
        }

        OnPropertyChanged(nameof(HasSnapshots));
    }

    [RelayCommand]
    private async Task TakeSnapshotAsync()
    {
        string tag = (NewSnapshotName ?? string.Empty).Trim();
        if (tag.Length == 0 || tag.Any(char.IsWhiteSpace))
        {
            StatusMessage = "Enter a snapshot name with no spaces.";
            return;
        }

        if (!CanManageSnapshots || PrimaryDiskPath is not { } disk)
        {
            return;
        }

        try
        {
            await _snapshotService.CreateAsync(disk, tag);
            NewSnapshotName = null;
            await RefreshSnapshotsAsync();
        }
        catch (DiskException ex)
        {
            StatusMessage = $"Couldn't create the snapshot: {ex.Message}";
        }
    }

    // Restoring overwrites the current disk state, so it asks first.
    [RelayCommand]
    private void RestoreSnapshot(VmSnapshot snapshot) => SnapshotPendingRestore = snapshot;

    [RelayCommand]
    private void CancelRestoreSnapshot() => SnapshotPendingRestore = null;

    [RelayCommand]
    private async Task ConfirmRestoreSnapshotAsync()
    {
        VmSnapshot? snapshot = SnapshotPendingRestore;
        SnapshotPendingRestore = null;
        if (snapshot is null || !CanManageSnapshots || PrimaryDiskPath is not { } disk)
        {
            return;
        }

        try
        {
            await _snapshotService.RestoreAsync(disk, snapshot.Name);
            StatusMessage = $"Restored snapshot '{snapshot.Name}'.";
            await RefreshSnapshotsAsync();
        }
        catch (DiskException ex)
        {
            StatusMessage = $"Couldn't restore the snapshot: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteSnapshotAsync(VmSnapshot snapshot)
    {
        if (!CanManageSnapshots || PrimaryDiskPath is not { } disk)
        {
            return;
        }

        try
        {
            await _snapshotService.DeleteAsync(disk, snapshot.Name);
            await RefreshSnapshotsAsync();
        }
        catch (DiskException ex)
        {
            StatusMessage = $"Couldn't delete the snapshot: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void Delete() => IsConfirmingDelete = true;

    private bool CanDelete() => Status == VmStatus.Stopped;

    [RelayCommand]
    private async Task ConfirmDeleteAsync()
    {
        await _repository.DeleteAsync(Vm.Config.Id);
        IsConfirmingDelete = false;
        Deleted?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void CancelDelete() => IsConfirmingDelete = false;

    [RelayCommand(CanExecute = nameof(CanAttachIso))]
    private async Task ChooseIsoAsync()
    {
        string? iso = await _filePicker.PickIsoAsync();
        if (string.IsNullOrEmpty(iso))
        {
            return; // Cancelled.
        }

        // Attaching an installer implies booting from it first, then falling back to disk.
        await UpdateConfigAsync(config => config with
        {
            RemovableMedia = [new RemovableMediaConfig { Type = "cdrom", File = iso, Attached = true }],
            Boot = config.Boot with { Order = "dc" },
        });
    }

    [RelayCommand(CanExecute = nameof(CanRemoveIso))]
    private async Task RemoveIsoAsync()
    {
        // If the VM is running, eject from the live guest first (VirtualBox-style) so the
        // medium is gone now — e.g. the post-install "remove the installation medium" prompt.
        if (_session is not null)
        {
            try
            {
                await _session.EjectIsoAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                StatusMessage = $"Couldn't eject the ISO from the running VM: {ex.Message}";
            }
        }

        // Persist the detach (and disk-first boot) so the next launch is clean too.
        await UpdateConfigAsync(config => config with
        {
            RemovableMedia = [],
            Boot = config.Boot with { Order = "c" },
        });
    }

    // Attaching/swapping media is stopped-only; removal also works live (QMP eject).
    private bool CanAttachIso() => Status == VmStatus.Stopped;

    private bool CanRemoveIso() => Status is VmStatus.Stopped or VmStatus.Running or VmStatus.Paused;

    private async Task UpdateConfigAsync(Func<VmConfig, VmConfig> edit)
    {
        VmConfig updated = edit(Vm.Config);
        await _repository.SaveAsync(updated);
        ApplyConfig(updated);
    }

    /// <summary>
    /// Replaces this item's config in place (already persisted elsewhere) and refreshes
    /// the derived display. Used after the Settings panel saves; does not touch a running
    /// process — the live session keeps the config it launched with.
    /// </summary>
    public void ApplyConfig(VmConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        Vm = Vm with { Config = config };
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(IsoPath));
        OnPropertyChanged(nameof(HasIso));
        OnPropertyChanged(nameof(BootSummary));
    }

    private async Task TeardownSessionAsync()
    {
        IRunningVm? session = _session;
        _session = null;
        if (session is not null)
        {
            session.Exited -= OnSessionExited;
            await session.DisposeAsync();
        }
    }

    private void OnSessionExited(object? sender, EventArgs e) =>
        _dispatcher.Post(() =>
        {
            if (Status is VmStatus.Stopped or VmStatus.Stopping)
            {
                return; // A deliberate stop is already handling teardown.
            }

            Status = VmStatus.Stopped;
            StatusMessage = "The VM stopped unexpectedly (the guest powered off or the process exited).";
            _ = TeardownSessionAsync();
            _ = RefreshLogAsync();
        });

    private static string DescribeAccelerator(Accelerator accelerator) => accelerator switch
    {
        Accelerator.Tcg => "Running with software emulation (TCG) — no hardware acceleration, so expect slow performance.",
        Accelerator.Whpx => "Hardware acceleration via WHPX. On Windows, QEMU is generally slower than VMware or VirtualBox.",
        Accelerator.Kvm => "Hardware acceleration via KVM.",
        Accelerator.Hvf => "Hardware acceleration via Hypervisor.framework.",
        _ => string.Empty,
    };
}

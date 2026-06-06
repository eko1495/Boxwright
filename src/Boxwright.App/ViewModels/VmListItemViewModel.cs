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
    private readonly IEmbeddedVncDisplay _embeddedVnc;
    private readonly ILogReader _logReader;
    private readonly ISnapshotService _snapshotService;
    private readonly IVmCloneService _cloneService;
    private IRunningVm? _session;

    public VmListItemViewModel(
        Vm vm,
        IVmLauncher launcher,
        VmRepository repository,
        IUiDispatcher dispatcher,
        IFilePicker filePicker,
        IDisplayLauncher displayLauncher,
        IEmbeddedVncDisplay embeddedVnc,
        ILogReader logReader,
        ISnapshotService snapshotService,
        IVmCloneService cloneService)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(filePicker);
        ArgumentNullException.ThrowIfNull(displayLauncher);
        ArgumentNullException.ThrowIfNull(embeddedVnc);
        ArgumentNullException.ThrowIfNull(logReader);
        ArgumentNullException.ThrowIfNull(snapshotService);
        ArgumentNullException.ThrowIfNull(cloneService);

        Vm = vm;
        _launcher = launcher;
        _repository = repository;
        _dispatcher = dispatcher;
        _filePicker = filePicker;
        _displayLauncher = displayLauncher;
        _embeddedVnc = embeddedVnc;
        _logReader = logReader;
        _snapshotService = snapshotService;
        _cloneService = cloneService;
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
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(CanManageSnapshots), nameof(CanClone), nameof(CanSaveState))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand), nameof(PauseCommand),
        nameof(ResumeCommand), nameof(ResetCommand), nameof(DeleteCommand),
        nameof(ChooseIsoCommand), nameof(RemoveIsoCommand), nameof(OpenDisplayCommand), nameof(SaveStateCommand),
        nameof(RefreshGuestIpCommand))]
    private VmStatus _status = VmStatus.Stopped;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string? _statusMessage;

    // Live performance metrics (ADR-0019), polled while the VM runs. Histories are reassigned each tick so
    // the bound Sparkline redraws; the scalar texts label the current value.
    [ObservableProperty]
    private bool _hasMetrics;

    [ObservableProperty]
    private double[] _cpuHistory = [];

    [ObservableProperty]
    private double[] _memoryHistory = [];

    [ObservableProperty]
    private double[] _diskHistory = [];

    [ObservableProperty]
    private string? _cpuText;

    [ObservableProperty]
    private string? _memoryText;

    [ObservableProperty]
    private string? _diskText;

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

    /// <summary>Raised after this VM is cloned, carrying the new VM so the list can add it.</summary>
    public event EventHandler<Vm>? Cloned;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        Status = VmStatus.Starting;
        StatusMessage = null;
        try
        {
            IRunningVm session = await _launcher.StartAsync(Vm);
            AttachSession(session);

            // A from-scratch Windows install boots from the installer CD only if a key is pressed at the
            // firmware's "Press any key to boot from CD…" prompt. Auto-press it so the install is hands-free
            // (ADR-0015). Fire-and-forget; it stops itself once the install window passes.
            if (Vm.Config.WindowsInstallInProgress)
            {
                _ = SendBootMediaKeypressesAsync(session);
            }

            // A cold boot just happened; if a saved state exists, jump to it and consume it.
            if (HasSavedState)
            {
                try
                {
                    await session.LoadStateAsync(SavedStateTag);
                    await session.DeleteStateAsync(SavedStateTag);
                    HasSavedState = false;
                    StatusMessage = "Resumed from the saved state.";
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    StatusMessage = $"Couldn't resume the saved state; started fresh instead. {ex.Message}";
                }
            }
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

    // Wire a live session (freshly started or re-adopted on restart) into this item: subscribe to its
    // exit, hold it, and reflect Running. Shared by StartAsync and TryAdoptAsync.
    private void AttachSession(IRunningVm session)
    {
        session.Exited += OnSessionExited;
        _session = session;
        Status = VmStatus.Running;
        StatusMessage = DescribeAccelerator(session.Accelerator);
        _ = PollMetricsAsync(session); // live CPU/RAM/disk graphs while running (ADR-0019)
    }

    /// <summary>
    /// Reconnects to a still-running QEMU left behind by a previous app run (ADR-0014). A no-op if
    /// this item already has a live session or there is nothing to adopt; otherwise the VM shows
    /// Running again. A failed reconnect never breaks the list load — the VM simply stays Stopped.
    /// </summary>
    internal async Task TryAdoptAsync()
    {
        if (_session is not null)
        {
            return;
        }

        try
        {
            IRunningVm? session = await _launcher.AdoptAsync(Vm);
            if (session is null)
            {
                return;
            }

            AttachSession(session);
            StatusMessage = $"Reconnected. {DescribeAccelerator(session.Accelerator)}";
            await RefreshLogAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusMessage = $"Couldn't reconnect to the running VM: {ex.Message}";
        }
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

        // VNC guests render in-app; SPICE (and anything else) still opens in remote-viewer.
        if (string.Equals(_session.DisplayProtocol, "vnc", StringComparison.OrdinalIgnoreCase))
        {
            _embeddedVnc.Open(Name, "127.0.0.1", _session.SpicePort);
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

    /// <summary>The guest's IP addresses (via the guest agent), shown when known.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGuestAddresses))]
    private string? _guestAddresses;

    /// <summary>True once the guest agent has reported at least one address.</summary>
    public bool HasGuestAddresses => !string.IsNullOrEmpty(GuestAddresses);

    [RelayCommand(CanExecute = nameof(CanControlRunning))]
    private async Task RefreshGuestIpAsync()
    {
        if (_session is null)
        {
            return;
        }

        try
        {
            IReadOnlyList<string> addresses = await _session.GetGuestAddressesAsync();
            GuestAddresses = addresses.Count == 0 ? null : string.Join(", ", addresses);
            if (!HasGuestAddresses)
            {
                StatusMessage = "No guest IP yet — is qemu-guest-agent installed and the guest booted?";
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusMessage = $"Couldn't read the guest IP: {ex.Message}";
        }
    }

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
        bool savedState = false;
        if (CanManageSnapshots && PrimaryDiskPath is { } disk)
        {
            try
            {
                foreach (VmSnapshot snapshot in await _snapshotService.ListAsync(disk))
                {
                    if (string.Equals(snapshot.Name, SavedStateTag, StringComparison.Ordinal))
                    {
                        savedState = true; // the reserved suspend snapshot — surfaced as "saved state", not a user snapshot
                    }
                    else
                    {
                        Snapshots.Add(snapshot);
                    }
                }
            }
            catch (DiskException ex)
            {
                StatusMessage = $"Couldn't list snapshots: {ex.Message}";
            }
        }

        HasSavedState = savedState;
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

    // ---- Saved state (suspend/resume via savevm/loadvm) ----

    private const string SavedStateTag = "boxwright-saved-state";

    /// <summary>True when this VM has a suspended state on disk; Start resumes it.</summary>
    [ObservableProperty]
    private bool _hasSavedState;

    /// <summary>
    /// State can be saved only while running, for a VM with a qcow2 disk, and under an accelerator
    /// that can serialize VM state — KVM or TCG. WHPX (Windows) and HVF (macOS) block <c>savevm</c>
    /// ("state blocked due to non-migratable CPUID/XSAVE support"), so the action is gated off there.
    /// </summary>
    public bool CanSaveState =>
        Status == VmStatus.Running &&
        PrimaryDiskPath is not null &&
        _session is { Accelerator: Accelerator.Kvm or Accelerator.Tcg };

    [RelayCommand(CanExecute = nameof(CanSaveState))]
    private async Task SaveStateAsync()
    {
        if (_session is null)
        {
            return;
        }

        Status = VmStatus.Stopping;
        try
        {
            await _session.SaveStateAsync(SavedStateTag);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusMessage = $"Couldn't save the VM state: {ex.Message}";
            Status = VmStatus.Running; // the save failed — the VM is still running
            return;
        }

        // The state is on disk; power off. Next Start resumes from it.
        _session.ForceStop();
        await TeardownSessionAsync();
        Status = VmStatus.Stopped;
        await RefreshSnapshotsAsync();
    }

    [RelayCommand]
    private async Task DiscardSavedStateAsync()
    {
        if (!HasSavedState || PrimaryDiskPath is not { } disk)
        {
            return;
        }

        try
        {
            await _snapshotService.DeleteAsync(disk, SavedStateTag);
            HasSavedState = false;
            await RefreshSnapshotsAsync();
        }
        catch (DiskException ex)
        {
            StatusMessage = $"Couldn't discard the saved state: {ex.Message}";
        }
    }

    // ---- Clone (stopped-only) ----

    [ObservableProperty]
    private string? _cloneName;

    /// <summary>A VM can only be cloned while stopped (its disks must be quiescent).</summary>
    public bool CanClone => Status == VmStatus.Stopped;

    [RelayCommand]
    private Task FullCloneAsync() => CloneAsync(CloneMode.Full);

    [RelayCommand]
    private Task LinkedCloneAsync() => CloneAsync(CloneMode.Linked);

    private async Task CloneAsync(CloneMode mode)
    {
        if (!CanClone)
        {
            return;
        }

        string name = string.IsNullOrWhiteSpace(CloneName) ? $"{Name} (clone)" : CloneName.Trim();
        try
        {
            Vm clone = await _cloneService.CloneAsync(Vm, name, mode);
            CloneName = null;
            StatusMessage = mode == CloneMode.Linked
                ? $"Created linked clone '{name}'. Don't modify or delete this VM while the clone exists."
                : $"Created clone '{name}'.";
            Cloned?.Invoke(this, clone);
        }
        catch (Exception ex) when (ex is DiskException or IOException or VmConfigException)
        {
            StatusMessage = $"Couldn't clone the VM: {ex.Message}";
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
        GuestAddresses = null; // don't show a stale IP once the VM is gone
        ResetMetrics();
        IRunningVm? session = _session;
        _session = null;
        if (session is not null)
        {
            session.Exited -= OnSessionExited;
            await session.DisposeAsync();
        }
    }

    private const int MetricsHistoryLength = 60;
    private static readonly TimeSpan MetricsInterval = TimeSpan.FromSeconds(1);
    private readonly List<double> _cpuSamples = [];
    private readonly List<double> _memorySamples = [];
    private readonly List<double> _diskSamples = [];

    // Polls the live session ~every 1.5s while running, differencing successive samples into CPU %, RAM,
    // and disk MB/s, and publishing ring-buffered histories for the graphs. Fire-and-forget; stops when the
    // VM leaves Running/Paused or the session is replaced. Marshals UI updates through the dispatcher.
    private async Task PollMetricsAsync(IRunningVm session)
    {
        int vcpus = Math.Max(1, Vm.Config.Cpu.Sockets * Vm.Config.Cpu.Cores * Vm.Config.Cpu.Threads);
        VmMetricsSample? previous = null;
        long previousTicks = 0;

        while (ReferenceEquals(_session, session) && Status is VmStatus.Running or VmStatus.Paused)
        {
            await Task.Delay(MetricsInterval);
            if (!ReferenceEquals(_session, session) || Status is not (VmStatus.Running or VmStatus.Paused))
            {
                return;
            }

            VmMetricsSample sample;
            long nowTicks;
            try
            {
                sample = await session.GetMetricsSampleAsync();
                nowTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                continue; // transient QMP/process hiccup — try the next tick
            }

            if (previous is { } prev)
            {
                double wallSeconds = System.Diagnostics.Stopwatch.GetElapsedTime(previousTicks, nowTicks).TotalSeconds;
                if (wallSeconds > 0)
                {
                    VmMetricsRate rate = VmMetrics.Derive(prev, sample, wallSeconds, vcpus);
                    _dispatcher.Post(() => PublishMetrics(rate));
                }
            }

            previous = sample;
            previousTicks = nowTicks;
        }
    }

    private void PublishMetrics(VmMetricsRate rate)
    {
        Push(_cpuSamples, rate.CpuPercent);
        Push(_memorySamples, rate.MemoryMegabytes);
        Push(_diskSamples, rate.DiskMegabytesPerSecond);
        CpuHistory = [.. _cpuSamples];
        MemoryHistory = [.. _memorySamples];
        DiskHistory = [.. _diskSamples];
        CpuText = $"{rate.CpuPercent:0}%";
        MemoryText = rate.MemoryMegabytes >= 1000 ? $"{rate.MemoryMegabytes / 1000:0.0} GB" : $"{rate.MemoryMegabytes:0} MB";
        DiskText = $"{rate.DiskMegabytesPerSecond:0.0} MB/s";
        HasMetrics = true;
    }

    private static void Push(List<double> ring, double value)
    {
        ring.Add(value);
        if (ring.Count > MetricsHistoryLength)
        {
            ring.RemoveAt(0);
        }
    }

    private void ResetMetrics()
    {
        _cpuSamples.Clear();
        _memorySamples.Clear();
        _diskSamples.Clear();
        CpuHistory = [];
        MemoryHistory = [];
        DiskHistory = [];
        CpuText = null;
        MemoryText = null;
        DiskText = null;
        HasMetrics = false;
    }

    private void OnSessionExited(object? sender, EventArgs e) =>
        _dispatcher.Post(() =>
        {
            if (Status is VmStatus.Stopped or VmStatus.Stopping)
            {
                return; // A deliberate stop is already handling teardown.
            }

            Status = VmStatus.Stopped;

            if (Vm.Config.InstallBoot is not null || Vm.Config.WindowsInstallInProgress)
            {
                // The unattended install ran to completion and powered itself off (Linux: the seed's
                // `shutdown -P now`; Windows: the Autounattend's final `shutdown /s`). Graduate the VM to a
                // normal disk boot before the next start.
                StatusMessage = "Unattended install finished — start the VM to use it.";
                _ = FinalizeInstallAsync();
            }
            else
            {
                StatusMessage = "The VM stopped unexpectedly (the guest powered off or the process exited).";
            }

            _ = TeardownSessionAsync();
            _ = RefreshLogAsync();
        });

    // After an unattended install powers off, drop the install markers (the one-shot installer kernel boot
    // and the Windows-install flag), eject the installer media, and switch to disk-first boot so later
    // starts come up off the freshly installed OS.
    private Task FinalizeInstallAsync() => UpdateConfigAsync(c => c with
    {
        InstallBoot = null,
        WindowsInstallInProgress = false,
        Boot = c.Boot with { Order = "c" },
        RemovableMedia = [.. c.RemovableMedia.Select(m => m with { Attached = false })],
    });

    // How long the boot-from-CD auto-keypress keeps trying. The UEFI firmware (OVMF) shows
    // "Press any key to boot from CD…" only after POST — observed ~15-25 s in and the exact moment varies
    // with host speed and ISO size — so the window must be generous to land in it reliably. It still ends
    // well before Windows Setup's first reboot (minutes later), so those in-process reboots get no keypress
    // and fall through the prompt to the now-bootable disk. Once Setup has booted with its answer file the
    // install is non-interactive, so the extra Enters are harmless.
    private const int BootKeypressSeconds = 45;

    // Presses Enter once a second across the window above so the firmware's "Press any key to boot from CD…"
    // prompt is dismissed with no human present. Best-effort — it stops as soon as the VM leaves Running, the
    // session is replaced, or QMP goes away.
    private async Task SendBootMediaKeypressesAsync(IRunningVm session)
    {
        for (int i = 0; i < BootKeypressSeconds; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
            if (!ReferenceEquals(_session, session) || Status is not (VmStatus.Running or VmStatus.Starting))
            {
                return;
            }

            try
            {
                await session.SendKeysAsync(["ret"]);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return;
            }
        }
    }

    private static string DescribeAccelerator(Accelerator accelerator) => accelerator switch
    {
        Accelerator.Tcg => "Running with software emulation (TCG) — no hardware acceleration, so expect slow performance.",
        Accelerator.Whpx => "Hardware acceleration via WHPX. On Windows, QEMU is generally slower than VMware or VirtualBox.",
        Accelerator.Kvm => "Hardware acceleration via KVM.",
        Accelerator.Hvf => "Hardware acceleration via Hypervisor.framework.",
        _ => string.Empty,
    };
}

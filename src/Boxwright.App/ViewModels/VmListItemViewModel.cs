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
    private IRunningVm? _session;

    public VmListItemViewModel(
        Vm vm,
        IVmLauncher launcher,
        VmRepository repository,
        IUiDispatcher dispatcher,
        IFilePicker filePicker,
        IDisplayLauncher displayLauncher)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(filePicker);
        ArgumentNullException.ThrowIfNull(displayLauncher);

        Vm = vm;
        _launcher = launcher;
        _repository = repository;
        _dispatcher = dispatcher;
        _filePicker = filePicker;
        _displayLauncher = displayLauncher;
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
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand), nameof(PauseCommand),
        nameof(ResumeCommand), nameof(ResetCommand), nameof(DeleteCommand),
        nameof(ChooseIsoCommand), nameof(RemoveIsoCommand), nameof(OpenDisplayCommand))]
    private VmStatus _status = VmStatus.Stopped;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isConfirmingDelete;

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
    }

    private bool CanStart() => Status == VmStatus.Stopped;

    [RelayCommand(CanExecute = nameof(CanControlRunning))]
    private async Task StopAsync()
    {
        Status = VmStatus.Stopping;
        try
        {
            if (_session is not null)
            {
                await _session.StopAsync(TimeSpan.FromSeconds(5));
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
        }
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

    [RelayCommand(CanExecute = nameof(CanEditMedia))]
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

    [RelayCommand(CanExecute = nameof(CanEditMedia))]
    private Task RemoveIsoAsync() =>
        UpdateConfigAsync(config => config with
        {
            RemovableMedia = [],
            Boot = config.Boot with { Order = "c" },
        });

    // Boot media can only be changed while the VM is stopped.
    private bool CanEditMedia() => Status == VmStatus.Stopped;

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

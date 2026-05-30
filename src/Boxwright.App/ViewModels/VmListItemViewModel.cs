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
/// A stateful row in the VM list: display info plus the power controls (start, stop,
/// pause/resume, reset, delete) wired to <see cref="IVmLauncher"/> / <see cref="IRunningVm"/>.
/// Holds the live session while the VM runs, reflects the resulting state, and is honest
/// about acceleration (Directive 9). Delete is two-step to guard the destructive action.
/// </summary>
public sealed partial class VmListItemViewModel : ObservableObject
{
    private readonly IVmLauncher _launcher;
    private readonly VmRepository _repository;
    private readonly IUiDispatcher _dispatcher;
    private IRunningVm? _session;

    public VmListItemViewModel(Vm vm, IVmLauncher launcher, VmRepository repository, IUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(dispatcher);

        Vm = vm;
        _launcher = launcher;
        _repository = repository;
        _dispatcher = dispatcher;
    }

    /// <summary>The underlying domain VM.</summary>
    public Vm Vm { get; }

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand), nameof(PauseCommand),
        nameof(ResumeCommand), nameof(ResetCommand), nameof(DeleteCommand))]
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

using Boxwright.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Boxwright.App.ViewModels;

/// <summary>
/// View model for the main window. Hosts the VM list, the New-VM flow, and host
/// context (the detected accelerator and the VMs folder) shown in the status bar.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly VmRepository _repository;
    private readonly IDiskService _diskService;

    public MainWindowViewModel(
        VmListViewModel vms,
        AcceleratorDetector acceleratorDetector,
        VmRepository repository,
        IDiskService diskService)
    {
        ArgumentNullException.ThrowIfNull(vms);
        ArgumentNullException.ThrowIfNull(acceleratorDetector);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(diskService);

        Vms = vms;
        _repository = repository;
        _diskService = diskService;
        Accelerator = acceleratorDetector.Detect().ToQemuValue();
        VmsDirectory = repository.RootDirectory;
    }

    /// <summary>The VM list shown as the window's main content.</summary>
    public VmListViewModel Vms { get; }

    /// <summary>The application name (window title and toolbar heading).</summary>
    public string Title { get; } = "Boxwright";

    /// <summary>The acceleration backend detected for this host (kvm/hvf/whpx/tcg).</summary>
    public string Accelerator { get; }

    /// <summary>Where this machine's VMs are stored on disk.</summary>
    public string VmsDirectory { get; }

    /// <summary>The active New-VM form, or null when not creating.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCreating))]
    private NewVmViewModel? _creation;

    /// <summary>True while the New-VM form is shown.</summary>
    public bool IsCreating => Creation is not null;

    [RelayCommand]
    private void BeginCreate()
    {
        if (Creation is not null)
        {
            return;
        }

        var form = new NewVmViewModel(_repository, _diskService, IsNameTaken);
        form.Created += OnVmCreated;
        form.Cancelled += OnCreateCancelled;
        Creation = form;
    }

    private bool IsNameTaken(string name) =>
        Vms.Vms.Any(i => string.Equals(i.Vm.Config.Name, name, StringComparison.OrdinalIgnoreCase));

    private void OnVmCreated(object? sender, Vm vm)
    {
        DetachCreation();
        Vms.AddCreated(vm);
    }

    private void OnCreateCancelled(object? sender, EventArgs e) => DetachCreation();

    private void DetachCreation()
    {
        if (Creation is null)
        {
            return;
        }

        Creation.Created -= OnVmCreated;
        Creation.Cancelled -= OnCreateCancelled;
        Creation = null;
    }
}

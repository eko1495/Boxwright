using Boxwright.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Boxwright.App.ViewModels;

/// <summary>
/// View model for the main window. Hosts the VM list and surfaces host context
/// (the detected accelerator and the VMs folder) in the status bar.
/// </summary>
public sealed class MainWindowViewModel : ObservableObject
{
    public MainWindowViewModel(VmListViewModel vms, AcceleratorDetector acceleratorDetector, VmRepository repository)
    {
        ArgumentNullException.ThrowIfNull(vms);
        ArgumentNullException.ThrowIfNull(acceleratorDetector);
        ArgumentNullException.ThrowIfNull(repository);

        Vms = vms;
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
}

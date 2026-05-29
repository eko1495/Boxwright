using Boxwright.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Boxwright.App.ViewModels;

/// <summary>
/// View model for the main window. For APP-1 this is a placeholder that proves the
/// composition root resolves Core services into view models; the VM list and its
/// actions arrive in later APP milestones.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    public MainWindowViewModel(AcceleratorDetector acceleratorDetector, VmRepository repository)
    {
        ArgumentNullException.ThrowIfNull(acceleratorDetector);
        ArgumentNullException.ThrowIfNull(repository);

        Accelerator = acceleratorDetector.Detect().ToQemuValue();
        VmsDirectory = repository.RootDirectory;
    }

    /// <summary>The application name, shown as the window title and heading.</summary>
    public string Title { get; } = "Boxwright";

    /// <summary>A short, honest description of what the app is.</summary>
    public string Tagline { get; } = "A cross-platform GUI for QEMU.";

    /// <summary>The acceleration backend detected for this host (kvm/hvf/whpx/tcg).</summary>
    public string Accelerator { get; }

    /// <summary>Where this machine's VMs are stored on disk.</summary>
    public string VmsDirectory { get; }
}

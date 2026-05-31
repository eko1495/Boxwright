using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Boxwright.App.Views;

/// <summary>
/// A VirtualBox-style confirmation shown when closing Boxwright while VMs are running:
/// shut the guests down cleanly, force them off, or cancel the close. A pure view concern;
/// the orchestration lives in <see cref="ViewModels.VmListViewModel"/>.
/// </summary>
public partial class CloseConfirmationDialog : Window
{
    public CloseConfirmationDialog() => InitializeComponent();

    public CloseConfirmationDialog(int runningCount)
        : this()
    {
        string subject = runningCount == 1 ? "virtual machine is" : "virtual machines are";
        MessageText.Text =
            $"{runningCount} {subject} still running.\n\n" +
            "Shut down — send each guest an ACPI power-off and wait for it to finish (recommended).\n\n" +
            "Force off — terminate immediately. This is like pulling the power cord and can corrupt the guest filesystem.";
    }

    private void OnShutDown(object? sender, RoutedEventArgs e) => Close(CloseChoice.ShutDown);

    private void OnForceOff(object? sender, RoutedEventArgs e) => Close(CloseChoice.ForceOff);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(CloseChoice.Cancel);
}

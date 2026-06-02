using Avalonia.Controls;

namespace Boxwright.App.Views;

/// <summary>
/// The "VM settings" form (boot-time settings). View-only; its <c>DataContext</c> is the
/// bound <see cref="ViewModels.VmSettingsViewModel"/> supplied by the host DataTemplate.
/// </summary>
public partial class VmSettingsView : UserControl
{
    public VmSettingsView() => InitializeComponent();
}

using Avalonia.Controls;

namespace Boxwright.App.Views;

/// <summary>
/// The detail pane for the selected VM: header + status pill, the power/control action
/// row, status/saved-state banners, the Media/Snapshots/Clone/Logs sections, and the
/// danger-zone delete. View-only; its <c>DataContext</c> is the bound
/// <see cref="ViewModels.VmListItemViewModel"/> supplied by the host DataTemplate.
/// </summary>
public partial class VmDetailView : UserControl
{
    public VmDetailView() => InitializeComponent();
}

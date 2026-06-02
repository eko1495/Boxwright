using Avalonia.Controls;

namespace Boxwright.App.Views;

/// <summary>
/// The guided "New VM" form. View-only; its <c>DataContext</c> is the bound
/// <see cref="ViewModels.NewVmViewModel"/> supplied by the host DataTemplate.
/// </summary>
public partial class NewVmView : UserControl
{
    public NewVmView() => InitializeComponent();
}

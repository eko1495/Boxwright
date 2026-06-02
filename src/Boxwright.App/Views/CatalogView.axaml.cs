using Avalonia.Controls;

namespace Boxwright.App.Views;

/// <summary>
/// The "Get an OS" gallery. View-only; its <c>DataContext</c> is the bound
/// <see cref="ViewModels.CatalogViewModel"/> supplied by the host DataTemplate.
/// </summary>
public partial class CatalogView : UserControl
{
    public CatalogView() => InitializeComponent();
}

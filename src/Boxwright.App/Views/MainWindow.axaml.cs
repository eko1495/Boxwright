using System;
using Avalonia.Controls;
using Boxwright.App.ViewModels;

namespace Boxwright.App.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    // Populate the list once the window is shown. Keeping the load in the view
    // model's command (rather than its constructor) keeps the VM testable and the
    // first frame responsive.
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.Vms.RefreshCommand.Execute(null);
        }
    }
}

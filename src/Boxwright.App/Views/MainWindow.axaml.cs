using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Boxwright.App.ViewModels;

namespace Boxwright.App.Views;

public partial class MainWindow : Window
{
    private bool _shutdownHandled;

    public MainWindow() => InitializeComponent();

    // Populate the list once the window is shown. Keeping the load in the view
    // model's command (rather than its constructor) keeps the VM testable and the
    // first frame responsive.
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        ApplyHostMaterial();
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.Vms.RefreshCommand.Execute(null);
        }
    }

    // On Windows 11 the OS grants Mica; let the material show by clearing the window
    // background. Everywhere else (Windows 10, Linux, macOS) ActualTransparencyLevel is
    // None, so we keep the opaque theme background — never a transparent, unreadable
    // window (Directive 4: degrade gracefully).
    private void ApplyHostMaterial()
    {
        if (ActualTransparencyLevel == WindowTransparencyLevel.Mica)
        {
            Background = Brushes.Transparent;
        }
    }

    // Don't let the app close out from under a running guest: confirm first, then shut the
    // VMs down (or force them off) before actually closing. A hard exit while a VM runs can
    // corrupt the guest filesystem, so the destructive choice is explicit (VirtualBox-style).
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_shutdownHandled || e.Cancel)
        {
            return;
        }

        if (DataContext is not MainWindowViewModel viewModel || !viewModel.Vms.HasRunningVms)
        {
            return; // Nothing running — close normally.
        }

        e.Cancel = true; // Hold the close while we ask and shut down.
        _ = HandleCloseAsync(viewModel.Vms);
    }

    private async Task HandleCloseAsync(VmListViewModel vms)
    {
        var dialog = new CloseConfirmationDialog(vms.RunningVms.Count);
        CloseChoice choice = await dialog.ShowDialog<CloseChoice>(this);
        switch (choice)
        {
            case CloseChoice.ShutDown:
                await vms.ShutDownAllAsync();
                break;
            case CloseChoice.ForceOff:
                await vms.ForceOffAllAsync();
                break;
            default:
                return; // Cancelled — keep the app open.
        }

        _shutdownHandled = true;
        Close();
    }

    // Cycle the app appearance: follow-OS (Default) -> Light -> Dark -> follow-OS.
    // FluentAvaloniaTheme re-renders live when the app's RequestedThemeVariant changes.
    private void OnToggleTheme(object? sender, RoutedEventArgs e)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        app.RequestedThemeVariant =
            app.RequestedThemeVariant == ThemeVariant.Light ? ThemeVariant.Dark
            : app.RequestedThemeVariant == ThemeVariant.Dark ? ThemeVariant.Default
            : ThemeVariant.Light;
    }
}

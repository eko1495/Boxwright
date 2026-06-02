using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using MarcusW.VncClient;
using MarcusW.VncClient.Protocol.Implementation.Services.Transports;
using MarcusW.VncClient.Protocol.SecurityTypes;
using MarcusW.VncClient.Security;
using Microsoft.Extensions.Logging.Abstractions;

namespace Boxwright.App.Views;

/// <summary>
/// An embedded VNC display: connects to a running VM's QEMU <c>-vnc</c> server and renders it
/// with the MarcusW.VncClient <c>VncView</c> control (which handles RFB encodings + keyboard/mouse).
/// View-only glue (cf. <see cref="CloseConfirmationDialog"/>); connects on open, disconnects on close.
/// </summary>
public sealed partial class VncDisplayWindow : Window, IDisposable
{
    private readonly string _host = string.Empty;
    private readonly int _port;
    private readonly CancellationTokenSource _cts = new();
    private RfbConnection? _connection;

    public VncDisplayWindow() => InitializeComponent();

    public VncDisplayWindow(string title, string host, int port)
        : this()
    {
        _host = host;
        _port = port;
        Title = $"Boxwright — {title}";
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (string.IsNullOrEmpty(_host))
        {
            return; // designer / no target
        }

        Vnc.Focus(); // keyboard input goes to the guest
        _ = ConnectAsync();
    }

    private async Task ConnectAsync()
    {
        try
        {
            var client = new VncClient(NullLoggerFactory.Instance);
            var parameters = new ConnectParameters
            {
                TransportParameters = new TcpTransportParameters { Host = _host, Port = _port },
                AuthenticationHandler = new NoAuthenticationHandler(),
            };

            _connection = await client.ConnectAsync(parameters, _cts.Token);
            Vnc.Connection = _connection;
            StatusBar.IsOpen = false; // connected — hide the banner
        }
        catch (OperationCanceledException)
        {
            // The window was closed while connecting — nothing to do.
        }
        catch (Exception ex)
        {
            StatusBar.Severity = InfoBarSeverity.Error;
            StatusBar.Title = "Display unavailable";
            StatusBar.Message = $"Couldn't connect to the VM display at {_host}:{_port}. {ex.Message}";
            StatusBar.IsOpen = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _cts.Cancel();
        Dispose();
    }

    /// <summary>Releases the live connection and the connect-cancellation source.</summary>
    public void Dispose()
    {
        _cts.Dispose();
        _connection?.Dispose();
    }

    // QEMU's -vnc (no password) negotiates RFB security "None", so this is never asked for input.
    // If a guest were ever configured with auth, fail clearly rather than hang.
    private sealed class NoAuthenticationHandler : IAuthenticationHandler
    {
        public Task<TInput> ProvideAuthenticationInputAsync<TInput>(
            RfbConnection connection, ISecurityType securityType, IAuthenticationInputRequest<TInput> request)
            where TInput : class, IAuthenticationInput =>
            throw new InvalidOperationException(
                $"VNC security type '{securityType?.Name}' requires credentials, but Boxwright connects without any (QEMU -vnc has no password).");
    }
}

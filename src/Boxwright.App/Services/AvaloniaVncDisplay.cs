using Boxwright.App.Views;

namespace Boxwright.App.Services;

/// <summary>Opens the embedded <see cref="VncDisplayWindow"/> (the <see cref="IEmbeddedVncDisplay"/> default).</summary>
internal sealed class AvaloniaVncDisplay : IEmbeddedVncDisplay
{
    public void Open(string title, string host, int port) =>
        new VncDisplayWindow(title, host, port).Show();
}

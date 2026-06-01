using System.Net.Sockets;
using Boxwright.Qmp;

namespace Boxwright.Core;

/// <summary>
/// Connects to a VM's guest-agent channel on demand (implemented by
/// <see cref="DefaultQgaConnector"/>). The agent is optional — it returns null when
/// qemu-guest-agent isn't installed/running in the guest, so callers degrade gracefully.
/// </summary>
public interface IQgaConnector
{
    /// <summary>
    /// Tries to connect to the guest agent on <paramref name="port"/> (loopback) and verify it
    /// responds within a short window. Returns a connected client the caller must dispose, or
    /// null when no responsive agent is present.
    /// </summary>
    Task<IQgaClient?> TryConnectAsync(int port, CancellationToken cancellationToken = default);
}

/// <summary>The default <see cref="IQgaConnector"/>: a short TCP connect + <c>guest-ping</c> probe.</summary>
public sealed class DefaultQgaConnector : IQgaConnector
{
    // The channel accepts immediately (QEMU listens); only the in-guest agent's reply is slow
    // or absent, so a short probe keeps "no agent" from stalling shutdown/IP requests.
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    /// <inheritdoc />
    public async Task<IQgaClient?> TryConnectAsync(int port, CancellationToken cancellationToken = default)
    {
        var client = new QgaClient();
        bool responsive = false;
        try
        {
            using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            probeCts.CancelAfter(ProbeTimeout);
            await client.ConnectAsync("127.0.0.1", port, probeCts.Token);
            responsive = await client.PingAsync(probeCts.Token);
            return responsive ? client : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // genuine caller cancellation — not a probe timeout
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException)
        {
            return null; // unreachable or no agent within the probe window
        }
        finally
        {
            if (!responsive)
            {
                await client.DisposeAsync();
            }
        }
    }
}

namespace Boxwright.Qmp;

/// <summary>
/// A QEMU Guest Agent (QGA) client (implemented by <see cref="QgaClient"/>): clean guest
/// shutdown and the guest's IP addresses, spoken over the agent's virtio-serial channel.
/// Separate from <see cref="IQmpClient"/> — QGA has no greeting or capabilities handshake.
/// </summary>
public interface IQgaClient : IAsyncDisposable
{
    /// <summary>Connects to the agent channel at <paramref name="host"/>:<paramref name="port"/> (no handshake).</summary>
    Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default);

    /// <summary>Returns true if the in-guest agent answers a ping; false if it isn't installed or running.</summary>
    Task<bool> PingAsync(CancellationToken cancellationToken = default);

    /// <summary>Requests a clean guest shutdown (<c>guest-shutdown</c>, powerdown). Fire-and-forget — the reply rarely arrives.</summary>
    Task ShutdownAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the guest's non-loopback IP addresses (<c>guest-network-get-interfaces</c>).</summary>
    Task<IReadOnlyList<string>> GetIpAddressesAsync(CancellationToken cancellationToken = default);
}

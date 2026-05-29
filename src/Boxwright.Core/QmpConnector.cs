using System.Net.Sockets;
using Boxwright.Qmp;

namespace Boxwright.Core;

/// <summary>The retry policy for establishing the QMP connection after a VM launch.</summary>
/// <param name="MaxAttempts">Maximum connect attempts before giving up.</param>
/// <param name="Delay">Delay between attempts.</param>
public sealed record QmpConnectRetryPolicy(int MaxAttempts, TimeSpan Delay)
{
    /// <summary>The default policy: up to 50 attempts, 100 ms apart (~5 s budget).</summary>
    public static QmpConnectRetryPolicy Default { get; } = new(50, TimeSpan.FromMilliseconds(100));
}

/// <summary>
/// Connects a QMP client to a freshly-launched QEMU, retrying while the socket
/// comes up (the startup race) and bailing fast if the process dies. Retry policy
/// lives here in Core, not in the GUI-agnostic Qmp client (architecture §4.2).
/// </summary>
public static class QmpConnector
{
    /// <summary>Connects to <paramref name="endpoint"/> with retries, returning the connected client.</summary>
    /// <exception cref="QmpProtocolException">The process died or attempts were exhausted before connecting.</exception>
    public static Task<IQmpClient> ConnectWithRetryAsync(
        QmpEndpoint endpoint,
        Func<bool> isProcessAlive,
        QmpConnectRetryPolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return ConnectWithRetryAsync(token => ConnectOnceAsync(endpoint, token), isProcessAlive, policy, cancellationToken);
    }

    /// <summary>
    /// Retry core: invokes <paramref name="connectAttempt"/> until it succeeds, the
    /// process dies (per <paramref name="isProcessAlive"/>), or attempts are exhausted.
    /// </summary>
    /// <exception cref="QmpProtocolException">The process died or attempts were exhausted before connecting.</exception>
    public static async Task<IQmpClient> ConnectWithRetryAsync(
        Func<CancellationToken, Task<IQmpClient>> connectAttempt,
        Func<bool> isProcessAlive,
        QmpConnectRetryPolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connectAttempt);
        ArgumentNullException.ThrowIfNull(isProcessAlive);
        ArgumentNullException.ThrowIfNull(policy);

        int attempt = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!isProcessAlive())
            {
                throw new QmpProtocolException("The QEMU process exited before its QMP socket became available.");
            }

            attempt++;
            try
            {
                return await connectAttempt(cancellationToken);
            }
            catch (Exception ex) when (ex is SocketException or IOException)
            {
                if (attempt >= policy.MaxAttempts)
                {
                    throw new QmpProtocolException($"Could not connect to the QMP socket after {attempt} attempts.", ex);
                }

                await Task.Delay(policy.Delay, cancellationToken);
            }
        }
    }

    private static async Task<IQmpClient> ConnectOnceAsync(QmpEndpoint endpoint, CancellationToken cancellationToken)
    {
        var client = new QmpClient();
        bool connected = false;
        try
        {
            await client.ConnectAsync(endpoint, cancellationToken);
            connected = true;
            return client;
        }
        finally
        {
            if (!connected)
            {
                await client.DisposeAsync();
            }
        }
    }
}

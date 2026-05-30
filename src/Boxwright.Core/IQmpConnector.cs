using Boxwright.Qmp;
using Microsoft.Extensions.Logging;

namespace Boxwright.Core;

/// <summary>Connects a QMP client to a launched VM. Injectable so the lifecycle is testable without a real socket.</summary>
public interface IQmpConnector
{
    /// <summary>Connects to <paramref name="endpoint"/>, retrying while the socket comes up and bailing if the process dies.</summary>
    Task<IQmpClient> ConnectAsync(QmpEndpoint endpoint, Func<bool> isProcessAlive, CancellationToken cancellationToken = default);
}

/// <summary>The default <see cref="IQmpConnector"/>, using <see cref="QmpConnector"/> with the default retry policy.</summary>
public sealed class DefaultQmpConnector : IQmpConnector
{
    private readonly ILogger<DefaultQmpConnector> _logger;

    /// <summary>Creates the connector. QMP traffic is logged through <paramref name="logger"/> at Debug level.</summary>
    public DefaultQmpConnector(ILogger<DefaultQmpConnector> logger) => _logger = logger;

    /// <inheritdoc />
    public Task<IQmpClient> ConnectAsync(QmpEndpoint endpoint, Func<bool> isProcessAlive, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return QmpConnector.ConnectWithRetryAsync(
            token => ConnectOnceAsync(endpoint, token),
            isProcessAlive,
            QmpConnectRetryPolicy.Default,
            cancellationToken);
    }

    private async Task<IQmpClient> ConnectOnceAsync(QmpEndpoint endpoint, CancellationToken cancellationToken)
    {
        // Trace hooks log the raw JSON both ways at Debug (off by default — flip the minimum
        // level to Debug to capture QMP traffic). The Qmp library itself stays dependency-free;
        // the hooks are how Core attaches logging without leaking a framework into the protocol.
        var client = new QmpClient(
            onSent: line => _logger.LogDebug("QMP > {Json}", line),
            onReceived: line => _logger.LogDebug("QMP < {Json}", line));
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

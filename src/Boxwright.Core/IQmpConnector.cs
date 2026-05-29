using Boxwright.Qmp;

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
    /// <inheritdoc />
    public Task<IQmpClient> ConnectAsync(QmpEndpoint endpoint, Func<bool> isProcessAlive, CancellationToken cancellationToken = default) =>
        QmpConnector.ConnectWithRetryAsync(endpoint, isProcessAlive, QmpConnectRetryPolicy.Default, cancellationToken);
}

using System.Text.Json;

namespace Boxwright.Qmp;

/// <summary>
/// A client for the QEMU Machine Protocol (QMP). Connects to a QEMU monitor
/// socket, performs the <c>qmp_capabilities</c> handshake, issues <c>execute</c>
/// commands whose replies are correlated by id, and surfaces asynchronous events.
/// </summary>
/// <remarks>
/// Implementations own the socket and a background read loop. Retry/policy logic
/// is intentionally left to the caller (e.g. Boxwright.Core), per the architecture.
/// </remarks>
public interface IQmpClient : IAsyncDisposable
{
    /// <summary>
    /// True once <see cref="ConnectAsync"/> has completed the capabilities
    /// handshake and the client is ready to execute commands.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// A hot stream of asynchronous QMP events (<c>SHUTDOWN</c>, <c>RESET</c>,
    /// <c>STOP</c>, <c>RESUME</c>, <c>POWERDOWN</c>, …). Slow consumers must never
    /// stall reply correlation.
    /// </summary>
    /// <remarks>
    /// The concrete event-delivery mechanism (a hand-rolled observable vs. a
    /// System.Reactive dependency vs. <c>IAsyncEnumerable</c>) is decided when the
    /// read loop is implemented (backlog QMP-5). The surface is kept dependency-free
    /// here: <see cref="IObservable{T}"/> is part of the BCL.
    /// </remarks>
    IObservable<QmpEvent> Events { get; }

    /// <summary>
    /// Connects to the endpoint, reads the QMP greeting, and sends
    /// <c>qmp_capabilities</c> to leave negotiation mode.
    /// </summary>
    Task ConnectAsync(QmpEndpoint endpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a QMP command and returns its raw <c>return</c> payload.
    /// </summary>
    /// <exception cref="QmpCommandException">QEMU replied with a QMP <c>error</c>.</exception>
    Task<JsonElement> ExecuteAsync(string command, object? arguments = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a QMP command and deserializes its <c>return</c> payload to
    /// <typeparamref name="TResult"/>.
    /// </summary>
    /// <exception cref="QmpCommandException">QEMU replied with a QMP <c>error</c>.</exception>
    Task<TResult> ExecuteAsync<TResult>(string command, object? arguments = null, CancellationToken cancellationToken = default);
}

using System.Net.Sockets;
using System.Text.Json;
using Boxwright.Qmp;
using Xunit;

namespace Boxwright.Core.Tests;

// CORE-8: connect-with-retry (the QMP startup race).
public class QmpConnectorTests
{
    private static readonly QmpConnectRetryPolicy FastPolicy = new(MaxAttempts: 5, Delay: TimeSpan.FromMilliseconds(1));

    [Fact]
    public async Task ConnectWithRetry_SucceedsAfterTransientFailures()
    {
        int calls = 0;
        var stub = new StubQmpClient();
        Func<CancellationToken, Task<IQmpClient>> attempt = _ =>
        {
            calls++;
            return calls <= 2 ? throw new SocketException() : Task.FromResult<IQmpClient>(stub);
        };

        IQmpClient result = await QmpConnector.ConnectWithRetryAsync(attempt, () => true, FastPolicy);

        Assert.Same(stub, result);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task ConnectWithRetry_WhenProcessDied_Throws()
    {
        await Assert.ThrowsAsync<QmpProtocolException>(() =>
            QmpConnector.ConnectWithRetryAsync(_ => throw new SocketException(), () => false, FastPolicy));
    }

    [Fact]
    public async Task ConnectWithRetry_ExhaustsAttempts_Throws()
    {
        int calls = 0;
        Func<CancellationToken, Task<IQmpClient>> attempt = _ =>
        {
            calls++;
            throw new SocketException();
        };

        await Assert.ThrowsAsync<QmpProtocolException>(() =>
            QmpConnector.ConnectWithRetryAsync(attempt, () => true, FastPolicy));

        Assert.Equal(5, calls);
    }

    [Fact]
    public async Task ConnectWithRetry_Endpoint_Unreachable_Throws()
    {
        // A Unix-socket path with nothing listening fails fast (no TCP connect latency),
        // exercising the real QmpClient connect-and-retry through the endpoint overload.
        QmpEndpoint endpoint = QmpEndpoint.UnixSocket(
            Path.Combine(Path.GetTempPath(), $"boxwright-absent-{Guid.NewGuid():N}.sock"));

        await Assert.ThrowsAsync<QmpProtocolException>(() =>
            QmpConnector.ConnectWithRetryAsync(endpoint, () => true, FastPolicy));
    }

    private sealed class StubQmpClient : IQmpClient
    {
        public bool IsConnected => true;

        public IObservable<QmpEvent> Events => throw new NotSupportedException();

        public Task ConnectAsync(QmpEndpoint endpoint, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<JsonElement> ExecuteAsync(string command, object? arguments = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TResult> ExecuteAsync<TResult>(string command, object? arguments = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<QmpSchema> GetSchemaAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

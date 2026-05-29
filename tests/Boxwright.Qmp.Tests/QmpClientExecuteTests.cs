using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace Boxwright.Qmp.Tests;

// QMP-4: correlated execute + background read loop, exercised against the fixture.
public class QmpClientExecuteTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsReturnPayload()
    {
        await using var server = FakeQmpServer.Start();
        server.OnCommand("query-name", "{\"name\":\"ubuntu\"}");
        await using var client = new QmpClient();
        await client.ConnectAsync(server.Endpoint);

        JsonElement result = await client.ExecuteAsync("query-name");

        Assert.Equal("ubuntu", result.GetProperty("name").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_Generic_DeserializesReturn()
    {
        await using var server = FakeQmpServer.Start();
        server.OnCommand("query-status", "{\"status\":\"running\",\"running\":true}");
        await using var client = new QmpClient();
        await client.ConnectAsync(server.Endpoint);

        StatusDto status = await client.ExecuteAsync<StatusDto>("query-status");

        Assert.Equal("running", status.Status);
        Assert.True(status.Running);
    }

    [Fact]
    public async Task ExecuteAsync_ErrorReply_ThrowsQmpCommandException()
    {
        await using var server = FakeQmpServer.Start();
        server.OnCommandError("eject", "DeviceNotFound", "no such device 'cd0'");
        await using var client = new QmpClient();
        await client.ConnectAsync(server.Endpoint);

        var ex = await Assert.ThrowsAsync<QmpCommandException>(() => client.ExecuteAsync("eject"));

        Assert.Equal("DeviceNotFound", ex.ErrorClass);
        Assert.Equal("no such device 'cd0'", ex.Description);
    }

    [Fact]
    public async Task ExecuteAsync_ConcurrentOutOfOrderReplies_CorrelateById()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = FakeQmpServer.Start();
        server.OnCommandGated("slow", "{\"which\":\"slow\"}", gate.Task);
        server.OnCommand("fast", "{\"which\":\"fast\"}");
        await using var client = new QmpClient();
        await client.ConnectAsync(server.Endpoint);

        Task<JsonElement> slow = client.ExecuteAsync("slow"); // sent first, reply withheld
        Task<JsonElement> fast = client.ExecuteAsync("fast"); // sent second, replies immediately

        JsonElement fastResult = await fast;
        Assert.Equal("fast", fastResult.GetProperty("which").GetString());
        Assert.False(slow.IsCompleted);

        gate.SetResult();
        JsonElement slowResult = await slow;
        Assert.Equal("slow", slowResult.GetProperty("which").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_InterleavedEvent_RoutedToEventsNotReply()
    {
        await using var server = FakeQmpServer.Start();
        server.OnCommand("query-status", "{\"running\":true}");
        await using var client = new QmpClient();
        await client.ConnectAsync(server.Endpoint);

        var observer = new RecordingObserver();
        using var subscription = client.Events.Subscribe(observer);

        await server.EmitEventAsync("RESET");
        JsonElement status = await client.ExecuteAsync("query-status");

        Assert.True(status.GetProperty("running").GetBoolean());
        Assert.Contains(observer.Events, e => e.Name == "RESET");
    }

    [Fact]
    public async Task DisposeAsync_CancelsInFlightExecuteCalls()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = FakeQmpServer.Start();
        server.OnCommandGated("hang", "{}", gate.Task);
        var client = new QmpClient();
        await client.ConnectAsync(server.Endpoint);

        Task<JsonElement> pending = client.ExecuteAsync("hang");
        await client.DisposeAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
        gate.SetResult(); // let the server's gated handler unwind cleanly
    }

    [Fact]
    public async Task ExecuteAsync_WhenNotConnected_Throws()
    {
        await using var client = new QmpClient();

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ExecuteAsync("query-status"));
    }

    private sealed record StatusDto(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("running")] bool Running);

    private sealed class RecordingObserver : IObserver<QmpEvent>
    {
        private readonly ConcurrentQueue<QmpEvent> _events = new();

        public IReadOnlyCollection<QmpEvent> Events => _events;

        public void OnNext(QmpEvent value) => _events.Enqueue(value);

        public void OnError(Exception error)
        {
        }

        public void OnCompleted()
        {
        }
    }
}

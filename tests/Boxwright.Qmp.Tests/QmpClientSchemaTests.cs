using Xunit;

namespace Boxwright.Qmp.Tests;

// QMP-7: query-qmp-schema capability probe.
public class QmpClientSchemaTests
{
    private const string CannedSchema =
        "[{\"name\":\"quit\",\"meta-type\":\"command\"}," +
        "{\"name\":\"query-status\",\"meta-type\":\"command\"}," +
        "{\"name\":\"SHUTDOWN\",\"meta-type\":\"event\"}," +
        "{\"name\":\"RunState\",\"meta-type\":\"enum\"}]";

    [Fact]
    public async Task GetSchemaAsync_ParsesCommandsAndEvents()
    {
        await using var server = FakeQmpServer.Start();
        server.OnCommand("query-qmp-schema", CannedSchema);
        await using var client = new QmpClient();
        await client.ConnectAsync(server.Endpoint);

        QmpSchema schema = await client.GetSchemaAsync();

        Assert.True(schema.HasCommand("quit"));
        Assert.True(schema.HasCommand("query-status"));
        Assert.False(schema.HasCommand("no-such-command"));
        Assert.True(schema.HasEvent("SHUTDOWN"));
        Assert.False(schema.HasEvent("quit"));
    }

    [Fact]
    public async Task GetSchemaAsync_ExposesCommandAndEventSets()
    {
        await using var server = FakeQmpServer.Start();
        server.OnCommand("query-qmp-schema", CannedSchema);
        await using var client = new QmpClient();
        await client.ConnectAsync(server.Endpoint);

        QmpSchema schema = await client.GetSchemaAsync();

        Assert.Contains("quit", schema.Commands);
        Assert.Contains("SHUTDOWN", schema.Events);
        Assert.Equal(2, schema.Commands.Count); // quit, query-status (RunState is an enum, not a command)
    }

    [Fact]
    public async Task GetSchemaAsync_IsCachedAfterFirstCall()
    {
        await using var server = FakeQmpServer.Start();
        server.OnCommand("query-qmp-schema", CannedSchema);
        await using var client = new QmpClient();
        await client.ConnectAsync(server.Endpoint);

        QmpSchema first = await client.GetSchemaAsync();
        QmpSchema second = await client.GetSchemaAsync();

        Assert.Same(first, second);
    }
}

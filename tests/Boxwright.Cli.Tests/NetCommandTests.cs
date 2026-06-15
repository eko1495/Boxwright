using System.Text.Json;
using Boxwright.Cli.Commands;
using Boxwright.Core;
using Xunit;

namespace Boxwright.Cli.Tests;

public sealed class NetCommandTests
{
    private static NetCommand Build(TempVmStore store, CapturingOutput output, IVmStatusProbe? probe = null) =>
        new(new VmResolver(store.Repository), store.Repository, probe ?? new FakeStatusProbe(), output.Cli);

    private static async Task<NetworkConfig> NetworkOfAsync(TempVmStore store) =>
        (await store.Repository.ListAsync())[0].Config.Network;

    [Fact]
    public async Task Show_DefaultsToUserMode()
    {
        using var store = new TempVmStore();
        store.Add("vm");
        var output = new CapturingOutput();

        await Build(store, output).RunAsync(ParsedArgs.Parse(["show", "vm"]), CancellationToken.None);

        Assert.Contains("Mode:   user", output.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Set_Bridge_PersistsModeAndBridge()
    {
        using var store = new TempVmStore();
        store.Add("vm");

        await Build(store, new CapturingOutput()).RunAsync(
            ParsedArgs.Parse(["set", "vm", "bridge", "--bridge", "lanbr"]), CancellationToken.None);

        NetworkConfig net = await NetworkOfAsync(store);
        Assert.Equal("bridge", net.Mode);
        Assert.Equal("lanbr", net.Bridge);
    }

    [Fact]
    public async Task Set_Bridge_DefaultsBridgeNameWhenOmitted()
    {
        using var store = new TempVmStore();
        store.Add("vm");

        await Build(store, new CapturingOutput()).RunAsync(ParsedArgs.Parse(["set", "vm", "bridge"]), CancellationToken.None);

        Assert.Equal("br0", (await NetworkOfAsync(store)).Bridge); // the config default
    }

    [Fact]
    public async Task Set_Tap_PersistsModeAndDevice()
    {
        using var store = new TempVmStore();
        store.Add("vm");

        await Build(store, new CapturingOutput()).RunAsync(
            ParsedArgs.Parse(["set", "vm", "tap", "--device", "tap5"]), CancellationToken.None);

        NetworkConfig net = await NetworkOfAsync(store);
        Assert.Equal("tap", net.Mode);
        Assert.Equal("tap5", net.TapDevice);
    }

    [Fact]
    public async Task Set_User_RevertsMode()
    {
        using var store = new TempVmStore();
        store.Add("vm");
        NetCommand cmd = Build(store, new CapturingOutput());
        await cmd.RunAsync(ParsedArgs.Parse(["set", "vm", "bridge"]), CancellationToken.None);

        await Build(store, new CapturingOutput()).RunAsync(ParsedArgs.Parse(["set", "vm", "user"]), CancellationToken.None);

        Assert.Equal("user", (await NetworkOfAsync(store)).Mode);
    }

    [Fact]
    public async Task Set_UnknownMode_IsAnError()
    {
        using var store = new TempVmStore();
        store.Add("vm");

        await Assert.ThrowsAsync<CliException>(() =>
            Build(store, new CapturingOutput()).RunAsync(ParsedArgs.Parse(["set", "vm", "frobnet"]), CancellationToken.None));
    }

    [Fact]
    public async Task Set_OnRunningVm_NotesNextBoot()
    {
        using var store = new TempVmStore();
        Vm vm = store.Add("vm");
        var probe = new FakeStatusProbe();
        probe.MarkRunning(vm.Config.Id);
        var output = new CapturingOutput();

        await Build(store, output, probe).RunAsync(ParsedArgs.Parse(["set", "vm", "user"]), CancellationToken.None);

        Assert.Contains("next boot", output.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Show_Json_EmitsTheNetworkShape()
    {
        using var store = new TempVmStore();
        store.Add("vm");
        var setOutput = new CapturingOutput();
        await Build(store, setOutput).RunAsync(ParsedArgs.Parse(["set", "vm", "bridge", "--bridge", "br9"]), CancellationToken.None);

        var output = new CapturingOutput();
        await Build(store, output).RunAsync(ParsedArgs.Parse(["show", "vm", "--json"]), CancellationToken.None);

        using JsonDocument doc = JsonDocument.Parse(output.Out);
        Assert.Equal("bridge", doc.RootElement.GetProperty("mode").GetString());
        Assert.Equal("br9", doc.RootElement.GetProperty("bridge").GetString());
    }

    [Fact]
    public async Task UnknownSubcommand_IsAnError()
    {
        using var store = new TempVmStore();

        await Assert.ThrowsAsync<CliException>(() =>
            Build(store, new CapturingOutput()).RunAsync(ParsedArgs.Parse(["frobnicate"]), CancellationToken.None));
    }
}

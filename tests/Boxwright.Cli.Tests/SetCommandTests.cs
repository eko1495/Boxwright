using Boxwright.Cli.Commands;
using Boxwright.Core;
using Xunit;

namespace Boxwright.Cli.Tests;

public sealed class SetCommandTests
{
    private static SetCommand Build(TempVmStore store, IVmStatusProbe? probe = null) =>
        new(new VmResolver(store.Repository), store.Repository, probe ?? new FakeStatusProbe(), new CapturingOutput().Cli);

    private static async Task<VmConfig> ReloadAsync(TempVmStore store, string id) =>
        (await store.Repository.ListAsync()).Single(v => v.Config.Id == id).Config;

    [Fact]
    public async Task Changes_only_the_supplied_settings()
    {
        using var store = new TempVmStore();
        Vm vm = store.Add("vm");
        SetCommand command = Build(store);

        int code = await command.RunAsync(
            ParsedArgs.Parse(["vm", "--memory", "8192", "--cpus", "6", "--firmware", "uefi"]),
            CancellationToken.None);

        Assert.Equal(0, code);
        VmConfig saved = await ReloadAsync(store, vm.Config.Id);
        Assert.Equal(8192, saved.MemoryMiB);
        Assert.Equal(6, saved.Cpu.Cores);
        Assert.Equal("uefi", saved.Firmware);
        Assert.Equal(vm.Config.Name, saved.Name); // untouched
    }

    [Fact]
    public async Task Renames_the_vm()
    {
        using var store = new TempVmStore();
        Vm vm = store.Add("old");
        SetCommand command = Build(store);

        await command.RunAsync(ParsedArgs.Parse(["old", "--name", "new"]), CancellationToken.None);

        Assert.Equal("new", (await ReloadAsync(store, vm.Config.Id)).Name);
    }

    [Fact]
    public async Task Rejects_a_name_taken_by_another_vm()
    {
        using var store = new TempVmStore();
        store.Add("taken");
        Vm vm = store.Add("vm");
        SetCommand command = Build(store);

        CliException ex = await Assert.ThrowsAsync<CliException>(() =>
            command.RunAsync(ParsedArgs.Parse(["vm", "--name", "taken"]), CancellationToken.None));

        Assert.Contains("already exists", ex.Message, StringComparison.Ordinal);
        Assert.Equal("vm", (await ReloadAsync(store, vm.Config.Id)).Name); // unchanged
    }

    [Fact]
    public async Task Sets_boolean_options()
    {
        using var store = new TempVmStore();
        Vm vm = store.Add("vm");
        SetCommand command = Build(store);

        await command.RunAsync(ParsedArgs.Parse(["vm", "--gl", "true", "--audio", "false", "--boot-menu", "true"]), CancellationToken.None);

        VmConfig saved = await ReloadAsync(store, vm.Config.Id);
        Assert.True(saved.Display.Gl);
        Assert.False(saved.Audio.Enabled);
        Assert.True(saved.Boot.Menu);
    }

    [Theory]
    [InlineData("--memory", "0", "positive")]
    [InlineData("--cpus", "-1", "positive")]
    [InlineData("--firmware", "coreboot", "one of")]
    [InlineData("--os-type", "plan9", "one of")]
    [InlineData("--display-protocol", "rdp", "one of")]
    [InlineData("--gl", "maybe", "true")]
    public async Task Rejects_invalid_values(string option, string value, string messagePart)
    {
        using var store = new TempVmStore();
        store.Add("vm");
        SetCommand command = Build(store);

        CliException ex = await Assert.ThrowsAsync<CliException>(() =>
            command.RunAsync(ParsedArgs.Parse(["vm", option, value]), CancellationToken.None));

        Assert.Contains(messagePart, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task No_options_is_a_usage_error()
    {
        using var store = new TempVmStore();
        store.Add("vm");
        SetCommand command = Build(store);

        await Assert.ThrowsAsync<CliException>(() =>
            command.RunAsync(ParsedArgs.Parse(["vm"]), CancellationToken.None));
    }

    [Fact]
    public async Task Editing_a_running_vm_is_allowed_and_noted()
    {
        using var store = new TempVmStore();
        Vm vm = store.Add("vm");
        var probe = new FakeStatusProbe();
        probe.MarkRunning(vm.Config.Id);
        var output = new CapturingOutput();
        var command = new SetCommand(new VmResolver(store.Repository), store.Repository, probe, output.Cli);

        await command.RunAsync(ParsedArgs.Parse(["vm", "--memory", "4096"]), CancellationToken.None);

        Assert.Equal(4096, (await ReloadAsync(store, vm.Config.Id)).MemoryMiB);
        Assert.Contains("next launch", output.Out, StringComparison.Ordinal);
    }
}

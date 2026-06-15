using Boxwright.Cli.Commands;
using Boxwright.Core;
using Xunit;

namespace Boxwright.Cli.Tests;

public sealed class TemplateCommandTests
{
    private static TemplateCommand Build(TempVmStore store, CapturingOutput output, FakeVmCloneService? clone = null, IVmStatusProbe? probe = null) =>
        new(new VmResolver(store.Repository), store.Repository, clone ?? new FakeVmCloneService(store), probe ?? new FakeStatusProbe(), output.Cli);

    private static async Task<Vm> SingleAsync(TempVmStore store, string name) =>
        (await store.Repository.ListAsync()).Single(v => v.Config.Name == name);

    [Fact]
    public async Task List_ShowsOnlyTemplates()
    {
        using var store = new TempVmStore();
        store.Add("plain");
        store.Add("tpl", isTemplate: true);
        var output = new CapturingOutput();

        await Build(store, output).RunAsync(ParsedArgs.Parse(["list"]), CancellationToken.None);

        Assert.Contains("tpl", output.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("plain", output.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_MarksAStoppedVmAsTemplate()
    {
        using var store = new TempVmStore();
        store.Add("base");

        await Build(store, new CapturingOutput()).RunAsync(ParsedArgs.Parse(["create", "base"]), CancellationToken.None);

        Assert.True((await SingleAsync(store, "base")).Config.IsTemplate);
    }

    [Fact]
    public async Task Create_RefusesARunningVm()
    {
        using var store = new TempVmStore();
        Vm vm = store.Add("base");
        var probe = new FakeStatusProbe();
        probe.MarkRunning(vm.Config.Id);

        await Assert.ThrowsAsync<CliException>(() =>
            Build(store, new CapturingOutput(), probe: probe).RunAsync(ParsedArgs.Parse(["create", "base"]), CancellationToken.None));

        Assert.False((await SingleAsync(store, "base")).Config.IsTemplate);
    }

    [Fact]
    public async Task New_LinkedByDefault_ClonesTheTemplate()
    {
        using var store = new TempVmStore();
        Vm tpl = store.Add("tpl", isTemplate: true);
        var clone = new FakeVmCloneService(store);

        await Build(store, new CapturingOutput(), clone).RunAsync(ParsedArgs.Parse(["new", "tpl", "web1"]), CancellationToken.None);

        (string sourceId, string newName, CloneMode mode) = Assert.Single(clone.Clones);
        Assert.Equal(tpl.Config.Id, sourceId);
        Assert.Equal("web1", newName);
        Assert.Equal(CloneMode.Linked, mode);
    }

    [Fact]
    public async Task New_FullFlag_ClonesFull()
    {
        using var store = new TempVmStore();
        store.Add("tpl", isTemplate: true);
        var clone = new FakeVmCloneService(store);

        await Build(store, new CapturingOutput(), clone).RunAsync(ParsedArgs.Parse(["new", "tpl", "web1", "--full"]), CancellationToken.None);

        Assert.Equal(CloneMode.Full, clone.Clones.Single().Mode);
    }

    [Fact]
    public async Task New_FromANonTemplate_IsRejected()
    {
        using var store = new TempVmStore();
        store.Add("plain"); // not a template
        var clone = new FakeVmCloneService(store);

        await Assert.ThrowsAsync<CliException>(() =>
            Build(store, new CapturingOutput(), clone).RunAsync(ParsedArgs.Parse(["new", "plain", "x"]), CancellationToken.None));

        Assert.Empty(clone.Clones);
    }

    [Fact]
    public async Task New_MissingName_IsAUsageError()
    {
        using var store = new TempVmStore();
        store.Add("tpl", isTemplate: true);

        await Assert.ThrowsAsync<CliException>(() =>
            Build(store, new CapturingOutput()).RunAsync(ParsedArgs.Parse(["new", "tpl"]), CancellationToken.None));
    }

    [Fact]
    public async Task Delete_RequiresYes_AndOnlyTargetsTemplates()
    {
        using var store = new TempVmStore();
        store.Add("tpl", isTemplate: true);
        store.Add("plain");

        // No --yes → refused, template kept.
        await Assert.ThrowsAsync<CliException>(() =>
            Build(store, new CapturingOutput()).RunAsync(ParsedArgs.Parse(["delete", "tpl"]), CancellationToken.None));
        Assert.Contains(await store.Repository.ListAsync(), v => v.Config.Name == "tpl");

        // A non-template is rejected by 'template delete'.
        await Assert.ThrowsAsync<CliException>(() =>
            Build(store, new CapturingOutput()).RunAsync(ParsedArgs.Parse(["delete", "plain", "--yes"]), CancellationToken.None));

        // With --yes → the template is deleted.
        await Build(store, new CapturingOutput()).RunAsync(ParsedArgs.Parse(["delete", "tpl", "--yes"]), CancellationToken.None);
        Assert.DoesNotContain(await store.Repository.ListAsync(), v => v.Config.Name == "tpl");
    }

    [Fact]
    public async Task UnknownSubcommand_IsAnError()
    {
        using var store = new TempVmStore();

        await Assert.ThrowsAsync<CliException>(() =>
            Build(store, new CapturingOutput()).RunAsync(ParsedArgs.Parse(["frobnicate"]), CancellationToken.None));
    }
}

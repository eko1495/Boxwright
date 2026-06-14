using Boxwright.Cli;
using Boxwright.Core;
using Xunit;

namespace Boxwright.Cli.Tests;

public sealed class VmResolverTests
{
    [Fact]
    public async Task Resolves_by_exact_id()
    {
        using var store = new TempVmStore();
        store.Add("alpha", id: "11111111-aaaa-bbbb-cccc-000000000001");
        var resolver = new VmResolver(store.Repository);

        Vm vm = await resolver.ResolveAsync("11111111-aaaa-bbbb-cccc-000000000001");

        Assert.Equal("alpha", vm.Config.Name);
    }

    [Fact]
    public async Task Resolves_by_exact_name_case_insensitively()
    {
        using var store = new TempVmStore();
        store.Add("DevBox");
        var resolver = new VmResolver(store.Repository);

        Vm vm = await resolver.ResolveAsync("devbox");

        Assert.Equal("DevBox", vm.Config.Name);
    }

    [Fact]
    public async Task Resolves_by_unique_id_prefix()
    {
        using var store = new TempVmStore();
        store.Add("alpha", id: "abc11111-0000-0000-0000-000000000000");
        store.Add("beta", id: "def22222-0000-0000-0000-000000000000");
        var resolver = new VmResolver(store.Repository);

        Vm vm = await resolver.ResolveAsync("abc");

        Assert.Equal("alpha", vm.Config.Name);
    }

    [Fact]
    public async Task Ambiguous_name_throws_listing_candidates()
    {
        using var store = new TempVmStore();
        store.Add("twin", id: "11111111-0000-0000-0000-000000000000");
        store.Add("twin", id: "22222222-0000-0000-0000-000000000000");
        var resolver = new VmResolver(store.Repository);

        CliException ex = await Assert.ThrowsAsync<CliException>(() => resolver.ResolveAsync("twin"));
        Assert.Contains("matches 2 VMs", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ambiguous_prefix_throws()
    {
        using var store = new TempVmStore();
        store.Add("a", id: "abc11111-0000-0000-0000-000000000000");
        store.Add("b", id: "abc22222-0000-0000-0000-000000000000");
        var resolver = new VmResolver(store.Repository);

        CliException ex = await Assert.ThrowsAsync<CliException>(() => resolver.ResolveAsync("abc"));
        Assert.Contains("ambiguous", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_reference_throws()
    {
        using var store = new TempVmStore();
        store.Add("alpha");
        var resolver = new VmResolver(store.Repository);

        await Assert.ThrowsAsync<CliException>(() => resolver.ResolveAsync("nope"));
    }

    [Fact]
    public async Task Exact_id_wins_over_a_name_collision()
    {
        using var store = new TempVmStore();
        // A VM whose *name* equals another VM's id should not shadow the exact-id match.
        string sharedId = "33333333-0000-0000-0000-000000000000";
        store.Add(sharedId, id: "44444444-0000-0000-0000-000000000000"); // name == sharedId
        store.Add("real", id: sharedId);
        var resolver = new VmResolver(store.Repository);

        Vm vm = await resolver.ResolveAsync(sharedId);

        Assert.Equal("real", vm.Config.Name);
    }
}

using System.Text.Json;
using Boxwright.Cli.Commands;
using Boxwright.Core;
using Xunit;

namespace Boxwright.Cli.Tests;

public sealed class OsCommandTests
{
    private static OsCatalogEntry Entry(string id, string name, bool autoinstall) => new()
    {
        Id = id,
        Name = name,
        Version = "1.0",
        Arch = "x86_64",
        IsoUrl = new Uri("https://example.invalid/os.iso"),
        SupportsAutoinstall = autoinstall,
    };

    [Fact]
    public async Task List_renders_catalog_entries()
    {
        var catalog = new FakeOsCatalogSource(Entry("ubuntu-x", "Ubuntu", true), Entry("fedora-y", "Fedora", false));
        var output = new CapturingOutput();
        var command = new OsCommand(catalog, output.Cli);

        int code = await command.RunAsync(ParsedArgs.Parse(["list"]), CancellationToken.None);

        Assert.Equal(0, code);
        Assert.Contains("ubuntu-x", output.Out, StringComparison.Ordinal);
        Assert.Contains("fedora-y", output.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_is_the_default_subcommand()
    {
        var catalog = new FakeOsCatalogSource(Entry("only", "Only", false));
        var output = new CapturingOutput();
        var command = new OsCommand(catalog, output.Cli);

        await command.RunAsync(ParsedArgs.Parse([]), CancellationToken.None);

        Assert.Contains("only", output.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Empty_catalog_says_so()
    {
        var output = new CapturingOutput();
        var command = new OsCommand(new FakeOsCatalogSource(), output.Cli);

        await command.RunAsync(ParsedArgs.Parse(["list"]), CancellationToken.None);

        Assert.Contains("empty", output.Out, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unknown_subcommand_is_an_error()
    {
        var command = new OsCommand(new FakeOsCatalogSource(), new CapturingOutput().Cli);

        await Assert.ThrowsAsync<CliException>(() =>
            command.RunAsync(ParsedArgs.Parse(["nonsense"]), CancellationToken.None));
    }

    private static OsCatalogEntry DetailedEntry() => new()
    {
        Id = "ubuntu-24.04-desktop",
        Name = "Ubuntu Desktop",
        Version = "24.04.4 LTS",
        Arch = "x86_64",
        IsoUrl = new Uri("https://releases.ubuntu.com/24.04/ubuntu.iso"),
        Sha256 = "abc123",
        SizeBytes = 6_000_000_000,
        SourceName = "Canonical",
        SupportsAutoinstall = true,
        OsFamily = "ubuntu",
        Notes = "Desktop image.",
        Recommended = new OsRecommendedSpec { MemoryMiB = 4096, CpuCores = 2, DiskGiB = 25, Firmware = "uefi" },
    };

    [Fact]
    public async Task Show_prints_full_details()
    {
        var catalog = new FakeOsCatalogSource(DetailedEntry());
        var output = new CapturingOutput();
        var command = new OsCommand(catalog, output.Cli);

        int code = await command.RunAsync(ParsedArgs.Parse(["show", "ubuntu-24.04-desktop"]), CancellationToken.None);

        Assert.Equal(0, code);
        string text = output.Out;
        Assert.Contains("https://releases.ubuntu.com/24.04/ubuntu.iso", text, StringComparison.Ordinal);
        Assert.Contains("abc123", text, StringComparison.Ordinal);
        Assert.Contains("4096 MiB · 2 vCPU · 25 GiB · uefi", text, StringComparison.Ordinal);
        Assert.Contains("Desktop image.", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Show_matches_the_id_case_insensitively()
    {
        var command = new OsCommand(new FakeOsCatalogSource(DetailedEntry()), new CapturingOutput().Cli);

        int code = await command.RunAsync(ParsedArgs.Parse(["show", "UBUNTU-24.04-DESKTOP"]), CancellationToken.None);

        Assert.Equal(0, code);
    }

    [Fact]
    public async Task Show_json_carries_url_size_and_recommended_specs()
    {
        var catalog = new FakeOsCatalogSource(DetailedEntry());
        var output = new CapturingOutput();
        var command = new OsCommand(catalog, output.Cli);

        await command.RunAsync(ParsedArgs.Parse(["show", "ubuntu-24.04-desktop", "--json"]), CancellationToken.None);

        JsonElement root = JsonDocument.Parse(output.Out).RootElement;
        Assert.Equal("ubuntu-24.04-desktop", root.GetProperty("id").GetString());
        Assert.Equal(6_000_000_000, root.GetProperty("sizeBytes").GetInt64());
        Assert.Equal(25, root.GetProperty("diskGiB").GetInt32());
        Assert.False(root.GetProperty("hasUnattendedRecipe").GetBoolean());
    }

    [Fact]
    public async Task Show_unknown_id_is_an_error()
    {
        var command = new OsCommand(new FakeOsCatalogSource(DetailedEntry()), new CapturingOutput().Cli);

        CliException ex = await Assert.ThrowsAsync<CliException>(() =>
            command.RunAsync(ParsedArgs.Parse(["show", "nope"]), CancellationToken.None));

        Assert.Contains("os list", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Show_requires_an_id()
    {
        var command = new OsCommand(new FakeOsCatalogSource(DetailedEntry()), new CapturingOutput().Cli);

        await Assert.ThrowsAsync<CliException>(() =>
            command.RunAsync(ParsedArgs.Parse(["show"]), CancellationToken.None));
    }

    [Fact]
    public async Task List_flags_a_stale_catalog_cache()
    {
        var catalog = new FreshnessAwareCatalog(
            new OsCatalogFreshness(OsCatalogFreshnessState.StaleCache, Age: TimeSpan.FromDays(40)),
            Entry("ubuntu-x", "Ubuntu", true));
        var output = new CapturingOutput();
        var command = new OsCommand(catalog, output.Cli);

        await command.RunAsync(ParsedArgs.Parse(["list"]), CancellationToken.None);

        Assert.Contains("stale", output.Out, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("40 day", output.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_does_not_flag_a_fresh_remote_catalog_as_stale()
    {
        var catalog = new FreshnessAwareCatalog(
            new OsCatalogFreshness(OsCatalogFreshnessState.Remote),
            Entry("ubuntu-x", "Ubuntu", true));
        var output = new CapturingOutput();
        var command = new OsCommand(catalog, output.Cli);

        await command.RunAsync(ParsedArgs.Parse(["list"]), CancellationToken.None);

        Assert.DoesNotContain("stale", output.Out, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task List_json_omits_the_freshness_note()
    {
        var catalog = new FreshnessAwareCatalog(
            new OsCatalogFreshness(OsCatalogFreshnessState.StaleCache, Age: TimeSpan.FromDays(40)),
            Entry("ubuntu-x", "Ubuntu", true));
        var output = new CapturingOutput();
        var command = new OsCommand(catalog, output.Cli);

        await command.RunAsync(ParsedArgs.Parse(["list", "--json"]), CancellationToken.None);

        Assert.DoesNotContain("Catalog:", output.Out, StringComparison.Ordinal); // --json stays a pure array
    }

    // A catalog source that also reports freshness, to exercise the CLI note.
    private sealed class FreshnessAwareCatalog(OsCatalogFreshness freshness, params OsCatalogEntry[] entries)
        : IOsCatalogSource, IOsCatalogFreshnessProvider
    {
        public Task<IReadOnlyList<OsCatalogEntry>> GetEntriesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OsCatalogEntry>>(entries);

        public OsCatalogFreshness GetFreshness() => freshness;
    }
}

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
}

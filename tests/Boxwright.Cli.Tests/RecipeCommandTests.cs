using System.Text.Json;
using Boxwright.Cli.Commands;
using Boxwright.Core;
using Xunit;

namespace Boxwright.Cli.Tests;

public sealed class RecipeCommandTests : IDisposable
{
    private readonly string _dir;
    private readonly LocalRecipeCatalogSource _recipes;

    public RecipeCommandTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"boxwright-recipecmd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _recipes = new LocalRecipeCatalogSource(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private RecipeCommand Build(CapturingOutput output) => new(_recipes, output.Cli);

    private string WriteRecipe(string file, string id)
    {
        string path = Path.Combine(_dir, file);
        File.WriteAllText(path,
            $$"""{ "schemaVersion": 1, "entries": [ { "id": "{{id}}", "name": "{{id}}", "version": "1", "arch": "x86_64", "isoUrl": "https://example.invalid/{{id}}.iso", "sha256": "a", "sourceName": "s" } ] }""");
        return path;
    }

    [Fact]
    public async Task Dir_PrintsTheRecipesFolder()
    {
        var output = new CapturingOutput();

        await Build(output).RunAsync(ParsedArgs.Parse(["dir"]), CancellationToken.None);

        Assert.Contains(_dir, output.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_Empty_SaysSo()
    {
        var output = new CapturingOutput();

        await Build(output).RunAsync(ParsedArgs.Parse(["list"]), CancellationToken.None);

        Assert.Contains("No recipes", output.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_ReportsOkAndBrokenRecipes()
    {
        WriteRecipe("ok.json", "alpine-3.21");
        File.WriteAllText(Path.Combine(_dir, "bad.json"), "{ not json");
        var output = new CapturingOutput();

        await Build(output).RunAsync(ParsedArgs.Parse(["list"]), CancellationToken.None);

        Assert.Contains("ok.json", output.Out, StringComparison.Ordinal);
        Assert.Contains("ok", output.Out, StringComparison.Ordinal);
        Assert.Contains("bad.json", output.Out, StringComparison.Ordinal);
        Assert.Contains("error", output.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_Json_ReportsPerFileStatus()
    {
        WriteRecipe("ok.json", "alpine-3.21");
        var output = new CapturingOutput();

        await Build(output).RunAsync(ParsedArgs.Parse(["list", "--json"]), CancellationToken.None);

        using JsonDocument doc = JsonDocument.Parse(output.Out);
        JsonElement entry = Assert.Single(doc.RootElement.EnumerateArray().ToArray());
        Assert.True(entry.GetProperty("ok").GetBoolean());
        Assert.Equal("alpine-3.21", entry.GetProperty("entries")[0].GetString());
    }

    [Fact]
    public async Task Validate_GoodRecipe_Succeeds()
    {
        string path = WriteRecipe("alpine.json", "alpine-3.21");
        var output = new CapturingOutput();

        int code = await Build(output).RunAsync(ParsedArgs.Parse(["validate", path]), CancellationToken.None);

        Assert.Equal(0, code);
        Assert.Contains("alpine-3.21", output.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Validate_BadRecipe_IsACleanError()
    {
        string path = Path.Combine(_dir, "bad.json");
        File.WriteAllText(path, "{ not json");

        await Assert.ThrowsAsync<CliException>(() =>
            Build(new CapturingOutput()).RunAsync(ParsedArgs.Parse(["validate", path]), CancellationToken.None));
    }

    [Fact]
    public async Task Validate_MissingFile_IsACleanError()
    {
        await Assert.ThrowsAsync<CliException>(() =>
            Build(new CapturingOutput()).RunAsync(ParsedArgs.Parse(["validate", Path.Combine(_dir, "nope.json")]), CancellationToken.None));
    }

    [Fact]
    public async Task UnknownSubcommand_IsAnError()
    {
        await Assert.ThrowsAsync<CliException>(() =>
            Build(new CapturingOutput()).RunAsync(ParsedArgs.Parse(["frobnicate"]), CancellationToken.None));
    }
}

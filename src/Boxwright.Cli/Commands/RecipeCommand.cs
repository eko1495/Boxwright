using Boxwright.Cli.Json;
using Boxwright.Core;

namespace Boxwright.Cli.Commands;

/// <summary>
/// Inspects local OS recipes (ADR-0026): <c>dir</c> prints the recipes folder, <c>list</c> reports each
/// <c>*.json</c> recipe and whether it parses, and <c>validate</c> checks a specific file. Recipes are
/// OS catalog documents that extend the catalog without a recompile; this command helps author them.
/// </summary>
internal sealed class RecipeCommand : ICliCommand
{
    private readonly LocalRecipeCatalogSource _recipes;
    private readonly CliOutput _output;

    public RecipeCommand(LocalRecipeCatalogSource recipes, CliOutput output)
    {
        ArgumentNullException.ThrowIfNull(recipes);
        ArgumentNullException.ThrowIfNull(output);
        _recipes = recipes;
        _output = output;
    }

    public string Name => "recipe";

    public string Summary => "Inspect local OS recipes (dir/list/validate).";

    public string Usage => "recipe <dir|list [--json]|validate <file>>";

    public async Task<int> RunAsync(ParsedArgs args, CancellationToken cancellationToken)
    {
        string sub = args.PositionalOrNull(0) ?? "list";

        return sub.ToLowerInvariant() switch
        {
            "dir" => Dir(),
            "list" => await ListAsync(args, cancellationToken),
            "validate" => await ValidateAsync(args, cancellationToken),
            _ => throw new CliException($"Unknown 'recipe' subcommand '{sub}'. Usage: boxwright {Usage}"),
        };
    }

    private int Dir()
    {
        _output.Line(_recipes.Directory);
        if (!Directory.Exists(_recipes.Directory))
        {
            _output.Line("(does not exist yet — create it and drop *.json recipe files in)");
        }

        return 0;
    }

    private async Task<int> ListAsync(ParsedArgs args, CancellationToken cancellationToken)
    {
        string[] files = Directory.Exists(_recipes.Directory)
            ? Directory.EnumerateFiles(_recipes.Directory, "*.json").Order(StringComparer.Ordinal).ToArray()
            : [];

        var results = new List<RecipeJson>(files.Length);
        foreach (string file in files)
        {
            results.Add(await InspectAsync(file, cancellationToken));
        }

        if (args.HasFlag("json"))
        {
            _output.Line(CliJson.Write(results.ToArray()));
            return 0;
        }

        if (results.Count == 0)
        {
            _output.Line($"No recipes in {_recipes.Directory}.");
            return 0;
        }

        var table = new TextTable("RECIPE", "ENTRIES", "STATUS");
        foreach (RecipeJson r in results)
        {
            table.AddRow(Path.GetFileName(r.File), r.Ok ? r.Entries.Length.ToString() : "-", r.Ok ? "ok" : $"error: {r.Error}");
        }

        _output.Out.Write(table.Render());
        return 0;
    }

    private async Task<int> ValidateAsync(ParsedArgs args, CancellationToken cancellationToken)
    {
        string path = args.PositionalOrNull(1)
            ?? throw new CliException($"Usage: boxwright {Usage}");
        if (!File.Exists(path))
        {
            throw new CliException($"Recipe file not found: {path}");
        }

        RecipeJson result = await InspectAsync(path, cancellationToken);
        if (!result.Ok)
        {
            throw new CliException($"Invalid recipe: {result.Error}");
        }

        _output.Line($"OK — {result.Entries.Length} OS entr{(result.Entries.Length == 1 ? "y" : "ies")}: {string.Join(", ", result.Entries)}");
        return 0;
    }

    // Parses one recipe file into a result record (never throws; a bad file is reported, not fatal).
    private static async Task<RecipeJson> InspectAsync(string file, CancellationToken cancellationToken)
    {
        try
        {
            string json = await File.ReadAllTextAsync(file, cancellationToken);
            OsCatalogDocument doc = OsCatalogJson.Deserialize(json);
            return new RecipeJson(file, true, doc.Entries.Select(e => e.Id).ToArray(), null);
        }
        catch (Exception ex) when (ex is OsCatalogException or IOException or UnauthorizedAccessException)
        {
            return new RecipeJson(file, false, [], ex.Message);
        }
    }
}

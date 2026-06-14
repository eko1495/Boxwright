using Boxwright.Cli.Json;
using Boxwright.Core;

namespace Boxwright.Cli.Commands;

/// <summary>
/// Browses the OS catalog. <c>os list</c> prints the installable images (id, name, version,
/// arch, autoinstall support) so scripts can see the catalog ids the GUI's one-click flow uses.
/// </summary>
internal sealed class OsCommand : ICliCommand
{
    private readonly IOsCatalogSource _catalog;
    private readonly CliOutput _output;

    public OsCommand(IOsCatalogSource catalog, CliOutput output)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(output);
        _catalog = catalog;
        _output = output;
    }

    public string Name => "os";

    public string Summary => "Browse the OS catalog (os list).";

    public string Usage => "os list [--json]";

    public async Task<int> RunAsync(ParsedArgs args, CancellationToken cancellationToken)
    {
        string sub = args.PositionalOrNull(0) ?? "list";
        if (!string.Equals(sub, "list", StringComparison.OrdinalIgnoreCase))
        {
            throw new CliException($"Unknown 'os' subcommand '{sub}'. Usage: boxwright {Usage}");
        }

        IReadOnlyList<OsCatalogEntry> entries = await _catalog.GetEntriesAsync(cancellationToken);

        if (args.HasFlag("json"))
        {
            OsEntryJson[] payload = entries
                .Select(e => new OsEntryJson(e.Id, e.Name, e.Version, e.Arch, e.SupportsAutoinstall))
                .ToArray();
            _output.Line(CliJson.Write(payload));
            return 0;
        }

        if (entries.Count == 0)
        {
            _output.Line("The OS catalog is empty.");
            return 0;
        }

        var table = new TextTable("ID", "NAME", "VERSION", "ARCH", "AUTOINSTALL");
        foreach (OsCatalogEntry entry in entries)
        {
            table.AddRow(
                entry.Id,
                entry.Name,
                entry.Version,
                entry.Arch,
                entry.SupportsAutoinstall ? "yes" : "no");
        }

        _output.Out.Write(table.Render());
        return 0;
    }
}

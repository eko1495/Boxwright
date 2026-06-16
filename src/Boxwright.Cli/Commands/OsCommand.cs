using Boxwright.Cli.Json;
using Boxwright.Core;

namespace Boxwright.Cli.Commands;

/// <summary>
/// Browses the OS catalog. <c>os list</c> prints the installable images (id, name, version, arch,
/// autoinstall support); <c>os show &lt;id&gt;</c> prints one entry's full details (URL, checksum, size,
/// provenance, recommended specs). Scripts use these to see the ids the GUI's one-click flow uses.
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

    public string Summary => "Browse the OS catalog (os list / os show).";

    public string Usage => "os <list [--json]|show <id> [--json]>";

    public async Task<int> RunAsync(ParsedArgs args, CancellationToken cancellationToken)
    {
        string sub = args.PositionalOrNull(0) ?? "list";
        return sub.ToLowerInvariant() switch
        {
            "list" => await ListAsync(args, cancellationToken),
            "show" => await ShowAsync(args, cancellationToken),
            _ => throw new CliException($"Unknown 'os' subcommand '{sub}'. Usage: boxwright {Usage}"),
        };
    }

    private async Task<int> ListAsync(ParsedArgs args, CancellationToken cancellationToken)
    {
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

        // A one-line freshness note (text output only — --json stays a pure entry array). Surfaces a stale
        // cache so the user isn't silently looking at possibly-outdated ISO URLs/SHA-256 (ADR-0020).
        if (_catalog is IOsCatalogFreshnessProvider freshness)
        {
            string? note = DescribeFreshness(freshness.GetFreshness());
            if (note is not null)
            {
                _output.Line(note);
            }
        }

        return 0;
    }

    private async Task<int> ShowAsync(ParsedArgs args, CancellationToken cancellationToken)
    {
        string id = args.PositionalOrNull(1)
            ?? throw new CliException($"Usage: boxwright {Usage}");

        IReadOnlyList<OsCatalogEntry> entries = await _catalog.GetEntriesAsync(cancellationToken);
        OsCatalogEntry entry = entries.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? throw new CliException($"No catalog entry with id '{id}'. Run 'boxwright os list' to see the ids.");

        bool hasRecipe = entry.Unattended is not null;

        if (args.HasFlag("json"))
        {
            var payload = new OsDetailJson(
                entry.Id, entry.Name, entry.Version, entry.Arch, entry.ImageKind,
                entry.IsoUrl.ToString(), entry.Sha256, entry.SizeBytes, entry.SourceName,
                entry.RequiresLicense, entry.OsFamily, entry.SupportsAutoinstall, hasRecipe, entry.Notes,
                entry.Recommended.MemoryMiB, entry.Recommended.CpuCores, entry.Recommended.DiskGiB, entry.Recommended.Firmware);
            _output.Line(CliJson.Write(payload));
            return 0;
        }

        string autoinstall = !entry.SupportsAutoinstall ? "no"
            : hasRecipe ? "yes (declarative recipe)"
            : "yes";

        _output.Line($"Id:           {entry.Id}");
        _output.Line($"Name:         {entry.Name}");
        _output.Line($"Version:      {entry.Version}");
        _output.Line($"Arch:         {entry.Arch}");
        _output.Line($"Image kind:   {entry.ImageKind}");
        _output.Line($"URL:          {entry.IsoUrl}");
        _output.Line($"SHA-256:      {entry.Sha256}");
        _output.Line($"Size:         {(entry.SizeBytes > 0 ? $"{ByteSize.Format(entry.SizeBytes)} ({entry.SizeBytes} bytes)" : "unknown")}");
        _output.Line($"Source:       {entry.SourceName}");
        _output.Line($"License:      {(entry.RequiresLicense ? "required (you supply it)" : "not required")}");
        _output.Line($"OS family:    {(string.IsNullOrEmpty(entry.OsFamily) ? "(unspecified)" : entry.OsFamily)}");
        _output.Line($"Autoinstall:  {autoinstall}");
        _output.Line($"Recommended:  {entry.Recommended.MemoryMiB} MiB · {entry.Recommended.CpuCores} vCPU · {entry.Recommended.DiskGiB} GiB · {entry.Recommended.Firmware}");
        if (!string.IsNullOrWhiteSpace(entry.Notes))
        {
            _output.Line($"Notes:        {entry.Notes}");
        }

        return 0;
    }

    private static string? DescribeFreshness(OsCatalogFreshness freshness)
    {
        // Clamp at 0: a cache file with a future mtime (clock skew, copied from another machine) would
        // otherwise print "cached -3 day(s) ago".
        int days = Math.Max(0, (int)(freshness.Age?.TotalDays ?? 0));
        return freshness.State switch
        {
            OsCatalogFreshnessState.Remote => "Catalog: served from the remote manifest.",
            OsCatalogFreshnessState.FreshCache => $"Catalog: cached {days} day(s) ago.",
            OsCatalogFreshnessState.StaleCache => $"Catalog: cached {days} day(s) ago (stale; remote unreachable, ISO URLs/SHA-256 may be outdated).",
            OsCatalogFreshnessState.Bundled => "Catalog: bundled baseline (remote and cache unavailable).",
            _ => null,
        };
    }
}

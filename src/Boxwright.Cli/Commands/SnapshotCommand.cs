using Boxwright.Cli.Json;
using Boxwright.Core;

namespace Boxwright.Cli.Commands;

/// <summary>
/// Manages a VM's offline qcow2 internal snapshots — across all of its qcow2 disks — via
/// <see cref="IVmSnapshotService"/>. <c>create</c>/<c>restore</c>/<c>delete</c> need exclusive image
/// access, so they require the VM to be stopped (live snapshots are a separate, GUI-side feature — ADR-0021).
/// </summary>
internal sealed class SnapshotCommand : ICliCommand
{
    private readonly VmResolver _resolver;
    private readonly IVmStatusProbe _statusProbe;
    private readonly IVmSnapshotService _snapshots;
    private readonly CliOutput _output;

    public SnapshotCommand(VmResolver resolver, IVmStatusProbe statusProbe, IVmSnapshotService snapshots, CliOutput output)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(statusProbe);
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(output);
        _resolver = resolver;
        _statusProbe = statusProbe;
        _snapshots = snapshots;
        _output = output;
    }

    public string Name => "snapshot";

    public string Summary => "Manage a VM's offline disk snapshots (list/create/restore/delete).";

    public string Usage => "snapshot <list [--json]|create|restore|delete> <id|name> [tag]";

    public async Task<int> RunAsync(ParsedArgs args, CancellationToken cancellationToken)
    {
        string sub = args.PositionalOrNull(0)
            ?? throw new CliException($"Usage: boxwright {Usage}");
        string reference = args.PositionalOrNull(1)
            ?? throw new CliException($"Usage: boxwright {Usage}");

        Vm vm = await _resolver.ResolveAsync(reference, cancellationToken);

        switch (sub.ToLowerInvariant())
        {
            case "list":
                return await ListAsync(vm, args.HasFlag("json"), cancellationToken);
            case "create":
                return await CreateAsync(vm, RequireTag(args), cancellationToken);
            case "restore":
                return await RestoreAsync(vm, RequireTag(args), cancellationToken);
            case "delete":
                return await DeleteAsync(vm, RequireTag(args), cancellationToken);
            default:
                throw new CliException($"Unknown 'snapshot' subcommand '{sub}'. Usage: boxwright {Usage}");
        }
    }

    private async Task<int> ListAsync(Vm vm, bool asJson, CancellationToken cancellationToken)
    {
        IReadOnlyList<VmSnapshot> snapshots = await _snapshots.ListAsync(vm, cancellationToken);

        if (asJson)
        {
            SnapshotJson[] payload = snapshots
                .Select(s => new SnapshotJson(s.Name, s.VmStateSize > 0, s.Created.ToString("yyyy-MM-ddTHH:mm:ssZ")))
                .ToArray();
            _output.Line(CliJson.Write(payload));
            return 0;
        }

        if (snapshots.Count == 0)
        {
            _output.Line("No snapshots.");
            return 0;
        }

        var table = new TextTable("TAG", "VM STATE", "CREATED");
        foreach (VmSnapshot snapshot in snapshots)
        {
            table.AddRow(
                snapshot.Name,
                snapshot.VmStateSize > 0 ? "yes" : "no",
                snapshot.Created.ToString("yyyy-MM-dd HH:mm:ss"));
        }

        _output.Out.Write(table.Render());
        return 0;
    }

    private async Task<int> CreateAsync(Vm vm, string tag, CancellationToken cancellationToken)
    {
        RequireStopped(vm, "create a snapshot");
        await _snapshots.CreateAsync(vm, tag, cancellationToken);
        _output.Line($"Created snapshot '{tag}' on '{vm.Config.Name}'.");
        return 0;
    }

    private async Task<int> RestoreAsync(Vm vm, string tag, CancellationToken cancellationToken)
    {
        RequireStopped(vm, "restore a snapshot");
        await _snapshots.RestoreAsync(vm, tag, cancellationToken);
        _output.Line($"Restored '{vm.Config.Name}' to snapshot '{tag}'.");
        return 0;
    }

    private async Task<int> DeleteAsync(Vm vm, string tag, CancellationToken cancellationToken)
    {
        RequireStopped(vm, "delete a snapshot");
        await _snapshots.DeleteAsync(vm, tag, cancellationToken);
        _output.Line($"Deleted snapshot '{tag}' from '{vm.Config.Name}'.");
        return 0;
    }

    private void RequireStopped(Vm vm, string action)
    {
        if (_statusProbe.IsRunning(vm))
        {
            throw new CliException(
                $"VM '{vm.Config.Name}' is running; stop it to {action} (offline snapshots need exclusive disk access).");
        }
    }

    private static string RequireTag(ParsedArgs args) =>
        args.PositionalOrNull(2) ?? throw new CliException("A snapshot tag is required.");
}

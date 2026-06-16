using Boxwright.Cli.Json;
using Boxwright.Core;

namespace Boxwright.Cli.Commands;

/// <summary>Lists every VM on disk with its run status and headline specs.</summary>
internal sealed class ListCommand : ICliCommand
{
    private readonly VmRepository _repository;
    private readonly IVmStatusProbe _statusProbe;
    private readonly IVmDiskUsageService _diskUsage;
    private readonly CliOutput _output;

    public ListCommand(VmRepository repository, IVmStatusProbe statusProbe, IVmDiskUsageService diskUsage, CliOutput output)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(statusProbe);
        ArgumentNullException.ThrowIfNull(diskUsage);
        ArgumentNullException.ThrowIfNull(output);
        _repository = repository;
        _statusProbe = statusProbe;
        _diskUsage = diskUsage;
        _output = output;
    }

    public string Name => "list";

    public string Summary => "List all VMs and their run status.";

    public string Usage => "list [--json]";

    public async Task<int> RunAsync(ParsedArgs args, CancellationToken cancellationToken)
    {
        List<Vm> vms = (await _repository.ListAsync(cancellationToken))
            .OrderBy(v => v.Config.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Measure on-disk footprint concurrently (best-effort: a VM whose disks can't be read reports null).
        VmDiskUsage[] usages = await Task.WhenAll(vms.Select(vm => _diskUsage.MeasureAsync(vm, cancellationToken)));

        if (args.HasFlag("json"))
        {
            VmSummaryJson[] payload = vms.Select((vm, i) => new VmSummaryJson(
                vm.Config.Id,
                vm.Config.Name,
                _statusProbe.IsRunning(vm) ? "running" : "stopped",
                vm.Config.OsType,
                vm.Config.Arch,
                vm.Config.MemoryMiB,
                Measured(usages[i]) ? usages[i].ActualBytes : null)).ToArray();
            _output.Line(CliJson.Write(payload));
            return 0;
        }

        if (vms.Count == 0)
        {
            _output.Line("No VMs found.");
            _output.Line($"VMs live under {_repository.RootDirectory}.");
            return 0;
        }

        var table = new TextTable("NAME", "ID", "STATUS", "OS", "ARCH", "MEMORY", "DISK");
        for (int i = 0; i < vms.Count; i++)
        {
            Vm vm = vms[i];
            table.AddRow(
                string.IsNullOrEmpty(vm.Config.Name) ? "(unnamed)" : vm.Config.Name,
                ShortId(vm.Config.Id),
                _statusProbe.IsRunning(vm) ? "running" : "stopped",
                vm.Config.OsType,
                vm.Config.Arch,
                $"{vm.Config.MemoryMiB} MiB",
                Measured(usages[i]) ? ByteSize.Format(usages[i].ActualBytes) : "—");
        }

        _output.Out.Write(table.Render());
        return 0;
    }

    // A usage report is worth showing only when at least one disk was actually measured.
    private static bool Measured(VmDiskUsage usage) => usage.Disks.Any(d => d.Measured);

    // The first GUID segment is plenty to identify a VM at a glance and to type as a prefix.
    internal static string ShortId(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return "(none)";
        }

        int dash = id.IndexOf('-', StringComparison.Ordinal);
        return dash > 0 ? id[..dash] : id;
    }
}

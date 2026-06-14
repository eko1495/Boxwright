using Boxwright.Core;

namespace Boxwright.Cli.Commands;

/// <summary>Lists every VM on disk with its run status and headline specs.</summary>
internal sealed class ListCommand : ICliCommand
{
    private readonly VmRepository _repository;
    private readonly IVmStatusProbe _statusProbe;
    private readonly CliOutput _output;

    public ListCommand(VmRepository repository, IVmStatusProbe statusProbe, CliOutput output)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(statusProbe);
        ArgumentNullException.ThrowIfNull(output);
        _repository = repository;
        _statusProbe = statusProbe;
        _output = output;
    }

    public string Name => "list";

    public string Summary => "List all VMs and their run status.";

    public string Usage => "list";

    public async Task<int> RunAsync(ParsedArgs args, CancellationToken cancellationToken)
    {
        IReadOnlyList<Vm> vms = await _repository.ListAsync(cancellationToken);
        if (vms.Count == 0)
        {
            _output.Line("No VMs found.");
            _output.Line($"VMs live under {_repository.RootDirectory}.");
            return 0;
        }

        var table = new TextTable("NAME", "ID", "STATUS", "OS", "ARCH", "MEMORY");
        foreach (Vm vm in vms.OrderBy(v => v.Config.Name, StringComparer.OrdinalIgnoreCase))
        {
            table.AddRow(
                string.IsNullOrEmpty(vm.Config.Name) ? "(unnamed)" : vm.Config.Name,
                ShortId(vm.Config.Id),
                _statusProbe.IsRunning(vm) ? "running" : "stopped",
                vm.Config.OsType,
                vm.Config.Arch,
                $"{vm.Config.MemoryMiB} MiB");
        }

        _output.Out.Write(table.Render());
        return 0;
    }

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

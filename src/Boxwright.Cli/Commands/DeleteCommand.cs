using Boxwright.Core;

namespace Boxwright.Cli.Commands;

/// <summary>Permanently deletes a VM's folder (config, disks, logs). Refuses while it's running.</summary>
internal sealed class DeleteCommand : ICliCommand
{
    private readonly VmResolver _resolver;
    private readonly VmRepository _repository;
    private readonly IVmStatusProbe _statusProbe;
    private readonly CliOutput _output;

    public DeleteCommand(VmResolver resolver, VmRepository repository, IVmStatusProbe statusProbe, CliOutput output)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(statusProbe);
        ArgumentNullException.ThrowIfNull(output);
        _resolver = resolver;
        _repository = repository;
        _statusProbe = statusProbe;
        _output = output;
    }

    public string Name => "delete";

    public string Summary => "Delete a VM and all its files.";

    public string Usage => "delete <id|name> --yes";

    public async Task<int> RunAsync(ParsedArgs args, CancellationToken cancellationToken)
    {
        string reference = args.PositionalOrNull(0)
            ?? throw new CliException($"Usage: boxwright {Usage}");
        Vm vm = await _resolver.ResolveAsync(reference, cancellationToken);

        if (_statusProbe.IsRunning(vm))
        {
            throw new CliException($"VM '{vm.Config.Name}' is running. Stop it first with 'boxwright stop'.");
        }

        if (!args.HasFlag("yes"))
        {
            throw new CliException(
                $"Refusing to delete '{vm.Config.Name}' ({vm.FolderPath}) without confirmation. " +
                "Re-run with --yes to permanently remove it.");
        }

        await _repository.DeleteAsync(vm.Config.Id, cancellationToken);
        _output.Line($"Deleted VM '{vm.Config.Name}' ({vm.Config.Id}).");
        return 0;
    }
}

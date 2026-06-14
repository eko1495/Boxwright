using Boxwright.Core;

namespace Boxwright.Cli.Commands;

/// <summary>
/// Clones a stopped VM into a new self-contained VM. <c>--linked</c> creates a space-efficient
/// qcow2 overlay backed by the source's disks (instant, but coupled to the source); the default is
/// a full, independent copy. The installer ISO is dropped and boot is repointed to disk by Core.
/// </summary>
internal sealed class CloneCommand : ICliCommand
{
    private readonly VmResolver _resolver;
    private readonly IVmStatusProbe _statusProbe;
    private readonly IVmCloneService _cloneService;
    private readonly CliOutput _output;

    public CloneCommand(VmResolver resolver, IVmStatusProbe statusProbe, IVmCloneService cloneService, CliOutput output)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(statusProbe);
        ArgumentNullException.ThrowIfNull(cloneService);
        ArgumentNullException.ThrowIfNull(output);
        _resolver = resolver;
        _statusProbe = statusProbe;
        _cloneService = cloneService;
        _output = output;
    }

    public string Name => "clone";

    public string Summary => "Clone a stopped VM (full copy, or --linked overlay).";

    public string Usage => "clone <id|name> <new-name> [--linked]";

    public async Task<int> RunAsync(ParsedArgs args, CancellationToken cancellationToken)
    {
        string reference = args.PositionalOrNull(0)
            ?? throw new CliException($"Usage: boxwright {Usage}");
        string newName = args.PositionalOrNull(1)
            ?? throw new CliException($"Usage: boxwright {Usage}");

        Vm source = await _resolver.ResolveAsync(reference, cancellationToken);
        if (_statusProbe.IsRunning(source))
        {
            throw new CliException($"VM '{source.Config.Name}' is running; stop it before cloning.");
        }

        CloneMode mode = args.HasFlag("linked") ? CloneMode.Linked : CloneMode.Full;
        Vm clone = await _cloneService.CloneAsync(source, newName, mode, cancellationToken);

        string kind = mode == CloneMode.Linked ? "linked" : "full";
        _output.Line($"Cloned '{source.Config.Name}' to '{clone.Config.Name}' ({clone.Config.Id}, {kind}).");
        if (mode == CloneMode.Linked)
        {
            _output.Line($"  Linked clone: keep the source VM ('{source.Config.Name}') and its disks in place.");
        }

        _output.Line($"  Folder: {clone.FolderPath}");
        return 0;
    }
}

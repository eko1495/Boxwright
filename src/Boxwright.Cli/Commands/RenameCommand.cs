using Boxwright.Core;

namespace Boxwright.Cli.Commands;

/// <summary>
/// Renames a VM from the headless shell: updates the display name <em>and</em> re-slugs the on-disk folder
/// to a browsable, human-readable name (ADR-0028) via <see cref="IVmRenameService"/>. The VM id is the
/// stable key and never changes. Refuses while the VM is running, and refuses to rename a VM that backs a
/// linked clone (moving its folder would corrupt the clone). For a display-name-only change that leaves the
/// folder alone, use <c>boxwright set &lt;vm&gt; --name</c>.
/// </summary>
internal sealed class RenameCommand : ICliCommand
{
    private readonly VmResolver _resolver;
    private readonly IVmRenameService _rename;
    private readonly IVmStatusProbe _statusProbe;
    private readonly CliOutput _output;

    public RenameCommand(VmResolver resolver, IVmRenameService rename, IVmStatusProbe statusProbe, CliOutput output)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(rename);
        ArgumentNullException.ThrowIfNull(statusProbe);
        ArgumentNullException.ThrowIfNull(output);
        _resolver = resolver;
        _rename = rename;
        _statusProbe = statusProbe;
        _output = output;
    }

    public string Name => "rename";

    public string Summary => "Rename a stopped VM (display name + browsable folder). Use 'set --name' to change the name only.";

    public string Usage => "rename <id|name> <new-name>";

    public async Task<int> RunAsync(ParsedArgs args, CancellationToken cancellationToken)
    {
        string reference = args.PositionalOrNull(0)
            ?? throw new CliException($"Usage: boxwright {Usage}");
        string newName = args.PositionalOrNull(1)?.Trim()
            ?? throw new CliException($"Usage: boxwright {Usage}");
        if (newName.Length == 0)
        {
            throw new CliException("The new name must not be empty.");
        }

        Vm vm = await _resolver.ResolveAsync(reference, cancellationToken);

        // The CLI's status probe PID-verifies the recorded process, so it's the authoritative running check
        // (stronger than Core's runtime.json-presence guard). Stop the rename before it touches the folder.
        if (_statusProbe.IsRunning(vm))
        {
            throw new CliException($"VM '{vm.Config.Name}' is running. Stop it first with 'boxwright stop'.");
        }

        string oldName = vm.Config.Name;
        Vm renamed;
        try
        {
            renamed = await _rename.RenameAsync(vm, newName, cancellationToken);
        }
        catch (VmHasDependentsException ex)
        {
            // Reuse the service's message verbatim — it already names the linked clones blocking the move.
            throw new CliException(ex.Message);
        }

        string folder = Path.GetFileName(renamed.FolderPath.TrimEnd(Path.DirectorySeparatorChar));
        _output.Line($"Renamed '{oldName}' -> '{renamed.Config.Name}' (folder: {folder}).");
        return 0;
    }
}

using Boxwright.Core;

namespace Boxwright.Cli.Commands;

/// <summary>
/// Opens a running VM's display by re-adopting its QEMU process (to learn the live display
/// port/protocol) and launching the external <c>remote-viewer</c> (ADR-0004). The adopted
/// handle is intentionally left undisposed — disposing would clear <c>runtime.json</c> and
/// mark the still-running VM as stopped.
/// </summary>
internal sealed class DisplayCommand : ICliCommand
{
    private readonly VmResolver _resolver;
    private readonly IVmLauncher _launcher;
    private readonly IDisplayLauncher _displayLauncher;
    private readonly CliOutput _output;

    public DisplayCommand(VmResolver resolver, IVmLauncher launcher, IDisplayLauncher displayLauncher, CliOutput output)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(displayLauncher);
        ArgumentNullException.ThrowIfNull(output);
        _resolver = resolver;
        _launcher = launcher;
        _displayLauncher = displayLauncher;
        _output = output;
    }

    public string Name => "display";

    public string Summary => "Open a running VM's display in remote-viewer.";

    public string Usage => "display <id|name>";

    public async Task<int> RunAsync(ParsedArgs args, CancellationToken cancellationToken)
    {
        string reference = args.PositionalOrNull(0)
            ?? throw new CliException($"Usage: boxwright {Usage}");
        Vm vm = await _resolver.ResolveAsync(reference, cancellationToken);

        IRunningVm? running = await _launcher.AdoptAsync(vm, cancellationToken);
        if (running is null)
        {
            throw new CliException($"VM '{vm.Config.Name}' is not running. Start it first with 'boxwright start'.");
        }

        // Don't dispose `running`: that would clear runtime.json and mark the live VM as stopped.
        _displayLauncher.Launch(running.SpicePort, running.DisplayProtocol);
        _output.Line($"Opened {running.DisplayProtocol} display for '{vm.Config.Name}' on 127.0.0.1:{running.SpicePort}.");
        return 0;
    }
}

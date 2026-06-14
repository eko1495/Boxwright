using Boxwright.Core;

namespace Boxwright.Cli.Commands;

/// <summary>
/// Stops a running VM by re-adopting its QEMU process (ADR-0014) and issuing a graceful
/// shutdown — or an immediate force-stop with <c>--force</c>. Disposing the adopted handle
/// clears the VM's <c>runtime.json</c>, so a subsequent <c>list</c> shows it stopped.
/// </summary>
internal sealed class StopCommand : ICliCommand
{
    private readonly VmResolver _resolver;
    private readonly IVmLauncher _launcher;
    private readonly CliOutput _output;

    public StopCommand(VmResolver resolver, IVmLauncher launcher, CliOutput output)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(output);
        _resolver = resolver;
        _launcher = launcher;
        _output = output;
    }

    public string Name => "stop";

    public string Summary => "Stop a running VM (graceful, or --force).";

    public string Usage => "stop <id|name> [--force] [--timeout=SECONDS]";

    public async Task<int> RunAsync(ParsedArgs args, CancellationToken cancellationToken)
    {
        string reference = args.PositionalOrNull(0)
            ?? throw new CliException($"Usage: boxwright {Usage}");
        Vm vm = await _resolver.ResolveAsync(reference, cancellationToken);

        IRunningVm? running = await _launcher.AdoptAsync(vm, cancellationToken);
        if (running is null)
        {
            _output.Line($"VM '{vm.Config.Name}' is not running.");
            return 0;
        }

        bool force = args.HasFlag("force");
        int timeoutSeconds = args.IntOption("timeout", 20);

        await using (running)
        {
            if (force)
            {
                running.ForceStop();
                _output.Line($"Force-stopped '{vm.Config.Name}'.");
            }
            else
            {
                _output.Line($"Stopping '{vm.Config.Name}' (graceful, up to {timeoutSeconds}s)…");
                await running.StopAsync(TimeSpan.FromSeconds(timeoutSeconds), cancellationToken);
                _output.Line($"Stopped '{vm.Config.Name}'.");
            }
        }

        return 0;
    }
}

using Boxwright.Core;

namespace Boxwright.Cli.Commands;

/// <summary>
/// Starts a VM. Foreground by default: the CLI stays attached (draining QEMU's output to the
/// per-VM log) until the guest exits or Ctrl+C triggers a graceful shutdown. <c>--detach</c>
/// returns immediately and leaves the VM running for a later <c>stop</c>/<c>display</c> (ADR-0022).
/// </summary>
internal sealed class StartCommand : ICliCommand
{
    private readonly VmResolver _resolver;
    private readonly IVmStatusProbe _statusProbe;
    private readonly IVmLauncher _launcher;
    private readonly IDisplayLauncher _displayLauncher;
    private readonly CliOutput _output;

    public StartCommand(
        VmResolver resolver,
        IVmStatusProbe statusProbe,
        IVmLauncher launcher,
        IDisplayLauncher displayLauncher,
        CliOutput output)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(statusProbe);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(displayLauncher);
        ArgumentNullException.ThrowIfNull(output);
        _resolver = resolver;
        _statusProbe = statusProbe;
        _launcher = launcher;
        _displayLauncher = displayLauncher;
        _output = output;
    }

    public string Name => "start";

    public string Summary => "Start a VM (foreground; --detach to leave it running).";

    public string Usage => "start <id|name> [--detach] [--display] [--timeout=SECONDS]";

    public async Task<int> RunAsync(ParsedArgs args, CancellationToken cancellationToken)
    {
        string reference = args.PositionalOrNull(0)
            ?? throw new CliException($"Usage: boxwright {Usage}");
        Vm vm = await _resolver.ResolveAsync(reference, cancellationToken);

        if (_statusProbe.IsRunning(vm))
        {
            throw new CliException($"VM '{vm.Config.Name}' is already running.");
        }

        bool detach = args.HasFlag("detach");
        bool openDisplay = args.HasFlag("display");
        int timeoutSeconds = args.IntOption("timeout", 20);

        // The launch itself shouldn't be aborted by a Ctrl+C meant to stop a foreground VM.
        IRunningVm running = await _launcher.StartAsync(vm, CancellationToken.None);

        _output.Line($"Started '{vm.Config.Name}' ({running.Accelerator} accelerator).");
        _output.Line($"  Display: {running.DisplayProtocol} on 127.0.0.1:{running.SpicePort}");

        if (openDisplay)
        {
            // Best-effort: a missing remote-viewer shouldn't fail the start.
            try
            {
                _displayLauncher.Launch(running.SpicePort, running.DisplayProtocol);
            }
            catch (DisplayException ex)
            {
                _output.ErrorLine($"Could not open the display viewer: {ex.Message}");
            }
        }

        if (detach)
        {
            // Leave QEMU running; runtime.json (written by the launcher) lets a later command re-adopt it.
            // Intentionally do NOT dispose `running` — disposing clears runtime state and drops our handle.
            _output.Line("Detached; the VM keeps running. Stop it with 'boxwright stop'.");
            return 0;
        }

        _output.Line("Running in the foreground. Press Ctrl+C to shut down.");
        await WaitForExitOrCancellationAsync(running, cancellationToken);

        if (cancellationToken.IsCancellationRequested && running.State != QemuProcessState.Exited)
        {
            _output.Line("Shutting down…");
            await running.StopAsync(TimeSpan.FromSeconds(timeoutSeconds), CancellationToken.None);
        }

        await running.DisposeAsync();
        _output.Line("VM stopped.");
        return 0;
    }

    // Completes when the guest process exits or the token is cancelled (Ctrl+C), whichever first.
    private static async Task WaitForExitOrCancellationAsync(IRunningVm running, CancellationToken cancellationToken)
    {
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnExited(object? sender, EventArgs e) => exited.TrySetResult();

        running.Exited += OnExited;
        try
        {
            // Guard the race where the process died before we subscribed.
            if (running.State == QemuProcessState.Exited)
            {
                return;
            }

            await using (cancellationToken.Register(() => exited.TrySetResult()))
            {
                await exited.Task;
            }
        }
        finally
        {
            running.Exited -= OnExited;
        }
    }
}

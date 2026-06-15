using Boxwright.Cli.Json;
using Boxwright.Core;

namespace Boxwright.Cli.Commands;

/// <summary>
/// Shows or sets a VM's network mode (ADR-0024): <c>user</c> (SLIRP NAT, the default), <c>bridge</c>
/// (join a host bridge), or <c>tap</c> (a pre-created TAP device). Bridge/TAP are Linux-only and take
/// effect on the VM's next boot. Host setup (the bridge / TAP / setuid helper) is the user's job.
/// </summary>
internal sealed class NetCommand : ICliCommand
{
    private static readonly string[] Modes = ["user", "bridge", "tap"];

    private readonly VmResolver _resolver;
    private readonly VmRepository _repository;
    private readonly IVmStatusProbe _statusProbe;
    private readonly CliOutput _output;

    public NetCommand(VmResolver resolver, VmRepository repository, IVmStatusProbe statusProbe, CliOutput output)
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

    public string Name => "net";

    public string Summary => "Show or set a VM's network mode (user/bridge/tap).";

    public string Usage => "net <show <id|name> [--json]|set <id|name> <user|bridge|tap> [--bridge=NAME] [--device=NAME]>";

    public async Task<int> RunAsync(ParsedArgs args, CancellationToken cancellationToken)
    {
        string sub = args.PositionalOrNull(0)
            ?? throw new CliException($"Usage: boxwright {Usage}");

        return sub.ToLowerInvariant() switch
        {
            "show" => await ShowAsync(args, cancellationToken),
            "set" => await SetAsync(args, cancellationToken),
            _ => throw new CliException($"Unknown 'net' subcommand '{sub}'. Usage: boxwright {Usage}"),
        };
    }

    private async Task<int> ShowAsync(ParsedArgs args, CancellationToken cancellationToken)
    {
        Vm vm = await ResolveAsync(args, cancellationToken);
        NetworkConfig net = vm.Config.Network;

        if (args.HasFlag("json"))
        {
            _output.Line(CliJson.Write(new NetworkJson(net.Mode, net.Model, net.Bridge, net.TapDevice)));
            return 0;
        }

        _output.Line($"Mode:   {net.Mode}");
        _output.Line($"Model:  {net.Model}");
        switch (net.Mode.ToLowerInvariant())
        {
            case "bridge":
                _output.Line($"Bridge: {net.Bridge}");
                break;
            case "tap":
                _output.Line($"TAP:    {net.TapDevice}");
                break;
            default:
                if (net.PortForwards.Count > 0)
                {
                    _output.Line("Port forwards (host → guest):");
                    foreach (PortForward f in net.PortForwards)
                    {
                        _output.Line($"  {f.HostPort} → {f.GuestPort}");
                    }
                }

                break;
        }

        return 0;
    }

    private async Task<int> SetAsync(ParsedArgs args, CancellationToken cancellationToken)
    {
        Vm vm = await ResolveAsync(args, cancellationToken);
        string mode = (args.PositionalOrNull(2) ?? throw new CliException($"Usage: boxwright {Usage}")).ToLowerInvariant();
        if (!Modes.Contains(mode))
        {
            throw new CliException($"Unknown network mode '{mode}'. Use one of: {string.Join(", ", Modes)}.");
        }

        NetworkConfig net = vm.Config.Network with
        {
            Mode = mode,
            Bridge = args.Option("bridge") is { Length: > 0 } b ? b : vm.Config.Network.Bridge,
            TapDevice = args.Option("device") is { Length: > 0 } t ? t : vm.Config.Network.TapDevice,
        };

        await _repository.SaveAsync(vm.Config with { Network = net }, cancellationToken);

        string detail = mode switch
        {
            "bridge" => $" (bridge {net.Bridge})",
            "tap" => $" (device {net.TapDevice})",
            _ => string.Empty,
        };
        _output.Line($"Set '{vm.Config.Name}' networking to {mode}{detail}.");

        if (NetworkValidation.RequiresLinux(mode) && !OperatingSystem.IsLinux())
        {
            _output.ErrorLine("  Note: bridge/TAP networking only launches on a Linux host.");
        }

        if (NetworkValidation.RequiresLinux(mode))
        {
            _output.Line("  Ensure the host bridge/TAP exists (Boxwright doesn't create it). See ADR-0024.");
        }

        if (_statusProbe.IsRunning(vm))
        {
            _output.Line("  The VM is running; it takes effect on next boot.");
        }

        return 0;
    }

    private Task<Vm> ResolveAsync(ParsedArgs args, CancellationToken cancellationToken)
    {
        string reference = args.PositionalOrNull(1)
            ?? throw new CliException($"Usage: boxwright {Usage}");
        return _resolver.ResolveAsync(reference, cancellationToken);
    }
}

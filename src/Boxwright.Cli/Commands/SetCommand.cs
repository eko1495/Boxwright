using Boxwright.Core;

namespace Boxwright.Cli.Commands;

/// <summary>
/// Edits a VM's boot-time configuration from the headless shell — the CLI counterpart to the GUI's
/// settings panel. Only the options supplied are changed; everything else (id, disks, removable media,
/// networking) is preserved. Settings take effect on the next launch, so editing a running VM is allowed
/// but flagged.
/// </summary>
internal sealed class SetCommand : ICliCommand
{
    private static readonly string[] Firmwares = ["bios", "uefi"];
    private static readonly string[] OsTypes = ["linux", "windows", "macos"];
    private static readonly string[] DisplayProtocols = ["spice", "vnc"];

    private readonly VmResolver _resolver;
    private readonly VmRepository _repository;
    private readonly IVmStatusProbe _statusProbe;
    private readonly CliOutput _output;

    public SetCommand(VmResolver resolver, VmRepository repository, IVmStatusProbe statusProbe, CliOutput output)
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

    public string Name => "set";

    public string Summary => "Edit a stopped VM's settings (memory, cpus, firmware, name, …).";

    public string Usage =>
        "set <id|name> [--name N] [--memory MiB] [--cpus N] [--firmware bios|uefi] [--os-type linux|windows|macos] " +
        "[--arch A] [--display-protocol spice|vnc] [--gl true|false] [--audio true|false] [--boot-menu true|false]";

    public async Task<int> RunAsync(ParsedArgs args, CancellationToken cancellationToken)
    {
        string reference = args.PositionalOrNull(0)
            ?? throw new CliException($"Usage: boxwright {Usage}");
        Vm vm = await _resolver.ResolveAsync(reference, cancellationToken);

        VmConfig config = vm.Config;
        var changes = new List<string>();

        if (args.Option("name") is { } rawName)
        {
            string name = rawName.Trim();
            if (name.Length == 0)
            {
                throw new CliException("--name must not be empty.");
            }

            if (await IsNameTakenByOtherAsync(name, vm.Config.Id, cancellationToken))
            {
                throw new CliException($"Another VM named '{name}' already exists.");
            }

            config = config with { Name = name };
            changes.Add($"name={name}");
        }

        if (args.Option("memory") is not null)
        {
            int memory = args.IntOption("memory", 0);
            if (memory <= 0)
            {
                throw new CliException("--memory must be a positive number of MiB.");
            }

            config = config with { MemoryMiB = memory };
            changes.Add($"memory={memory} MiB");
        }

        if (args.Option("cpus") is not null)
        {
            int cpus = args.IntOption("cpus", 0);
            if (cpus <= 0)
            {
                throw new CliException("--cpus must be a positive number of cores.");
            }

            config = config with { Cpu = config.Cpu with { Cores = cpus } };
            changes.Add($"cpus={cpus}");
        }

        if (args.Option("firmware") is { } firmware)
        {
            config = config with { Firmware = Choice("firmware", firmware, Firmwares) };
            changes.Add($"firmware={config.Firmware}");
        }

        if (args.Option("os-type") is { } osType)
        {
            config = config with { OsType = Choice("os-type", osType, OsTypes) };
            changes.Add($"os-type={config.OsType}");
        }

        if (args.Option("arch") is { } rawArch)
        {
            string arch = rawArch.Trim();
            if (arch.Length == 0)
            {
                throw new CliException("--arch must not be empty.");
            }

            config = config with { Arch = arch };
            changes.Add($"arch={arch}");
        }

        if (args.Option("display-protocol") is { } display)
        {
            config = config with { Display = config.Display with { Protocol = Choice("display-protocol", display, DisplayProtocols) } };
            changes.Add($"display-protocol={config.Display.Protocol}");
        }

        if (Bool(args, "gl") is { } gl)
        {
            config = config with { Display = config.Display with { Gl = gl } };
            changes.Add($"gl={gl.ToString().ToLowerInvariant()}");
        }

        if (Bool(args, "audio") is { } audio)
        {
            config = config with { Audio = config.Audio with { Enabled = audio } };
            changes.Add($"audio={audio.ToString().ToLowerInvariant()}");
        }

        if (Bool(args, "boot-menu") is { } bootMenu)
        {
            config = config with { Boot = config.Boot with { Menu = bootMenu } };
            changes.Add($"boot-menu={bootMenu.ToString().ToLowerInvariant()}");
        }

        if (changes.Count == 0)
        {
            throw new CliException($"Specify at least one setting to change. Usage: boxwright {Usage}");
        }

        await _repository.SaveAsync(config, cancellationToken);

        _output.Line($"Updated '{config.Name}': {string.Join(", ", changes)}.");
        if (_statusProbe.IsRunning(vm))
        {
            _output.Line("The VM is running; changes take effect on its next launch.");
        }

        return 0;
    }

    private async Task<bool> IsNameTakenByOtherAsync(string name, string selfId, CancellationToken cancellationToken)
    {
        IReadOnlyList<Vm> all = await _repository.ListAsync(cancellationToken);
        return all.Any(v =>
            !string.Equals(v.Config.Id, selfId, StringComparison.Ordinal) &&
            string.Equals(v.Config.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
    }

    // A value option constrained to a fixed set (case-insensitive), normalized to lowercase.
    private static string Choice(string option, string value, string[] allowed)
    {
        string normalized = value.Trim().ToLowerInvariant();
        if (!allowed.Contains(normalized))
        {
            throw new CliException($"--{option} must be one of: {string.Join(", ", allowed)} (got '{value}').");
        }

        return normalized;
    }

    // A true/false option. Returns null when absent; throws on a bare flag or a non-boolean value.
    private static bool? Bool(ParsedArgs args, string option)
    {
        string? raw = args.Option(option);
        if (raw is null)
        {
            if (args.HasFlag(option))
            {
                throw new CliException($"--{option} expects 'true' or 'false'.");
            }

            return null;
        }

        return raw.Trim().ToLowerInvariant() switch
        {
            "true" => true,
            "false" => false,
            _ => throw new CliException($"--{option} expects 'true' or 'false' (got '{raw}')."),
        };
    }
}

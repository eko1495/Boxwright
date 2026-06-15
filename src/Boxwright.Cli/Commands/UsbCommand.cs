using Boxwright.Cli.Json;
using Boxwright.Core;
using Boxwright.Qmp;

namespace Boxwright.Cli.Commands;

/// <summary>
/// Manages host USB passthrough (ADR-0023): <c>list</c> the host's USB devices (where the OS supports
/// it), <c>show</c> a VM's configured passthroughs, and <c>add</c>/<c>remove</c> them by vendor:product.
/// <c>add</c>/<c>remove</c> edit the persisted config and take effect on the VM's next boot.
/// </summary>
internal sealed class UsbCommand : ICliCommand
{
    private readonly IUsbDeviceEnumerator _enumerator;
    private readonly VmResolver _resolver;
    private readonly VmRepository _repository;
    private readonly IVmStatusProbe _statusProbe;
    private readonly IVmLauncher _launcher;
    private readonly CliOutput _output;

    public UsbCommand(
        IUsbDeviceEnumerator enumerator,
        VmResolver resolver,
        VmRepository repository,
        IVmStatusProbe statusProbe,
        IVmLauncher launcher,
        CliOutput output)
    {
        ArgumentNullException.ThrowIfNull(enumerator);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(statusProbe);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(output);
        _enumerator = enumerator;
        _resolver = resolver;
        _repository = repository;
        _statusProbe = statusProbe;
        _launcher = launcher;
        _output = output;
    }

    public string Name => "usb";

    public string Summary => "Manage host USB passthrough (list/show/add/remove).";

    public string Usage => "usb <list [--json]|show <id|name> [--json]|add <id|name> <vendor:product> [--description=TEXT] [--now]|remove <id|name> <vendor:product> [--now]>";

    public async Task<int> RunAsync(ParsedArgs args, CancellationToken cancellationToken)
    {
        string sub = args.PositionalOrNull(0)
            ?? throw new CliException($"Usage: boxwright {Usage}");

        return sub.ToLowerInvariant() switch
        {
            "list" => await ListHostAsync(args, cancellationToken),
            "show" => await ShowAsync(args, cancellationToken),
            "add" => await AddAsync(args, cancellationToken),
            "remove" => await RemoveAsync(args, cancellationToken),
            _ => throw new CliException($"Unknown 'usb' subcommand '{sub}'. Usage: boxwright {Usage}"),
        };
    }

    private async Task<int> ListHostAsync(ParsedArgs args, CancellationToken cancellationToken)
    {
        bool asJson = args.HasFlag("json");
        if (!_enumerator.IsSupported)
        {
            // Capability gate (Directive 4): degrade with a clear message; scripts still get valid JSON.
            _output.ErrorLine("Listing host USB devices isn't supported on this OS yet. Add a device by vendor:product with 'boxwright usb add'.");
            if (asJson)
            {
                _output.Line(CliJson.Write(Array.Empty<UsbJson>()));
            }

            return 0;
        }

        IReadOnlyList<HostUsbDevice> devices = await _enumerator.ListAsync(cancellationToken);
        if (asJson)
        {
            _output.Line(CliJson.Write(devices
                .Select(d => new UsbJson($"{d.VendorId}:{d.ProductId}", d.VendorId, d.ProductId, d.Description))
                .ToArray()));
            return 0;
        }

        if (devices.Count == 0)
        {
            _output.Line("No host USB devices found.");
            return 0;
        }

        var table = new TextTable("VENDOR:PRODUCT", "DESCRIPTION");
        foreach (HostUsbDevice d in devices)
        {
            table.AddRow($"{d.VendorId}:{d.ProductId}", d.Description.Length > 0 ? d.Description : "(unnamed)");
        }

        _output.Out.Write(table.Render());
        return 0;
    }

    private async Task<int> ShowAsync(ParsedArgs args, CancellationToken cancellationToken)
    {
        Vm vm = await ResolveAsync(args, cancellationToken);
        IReadOnlyList<UsbPassthroughConfig> devices = vm.Config.UsbDevices;

        if (args.HasFlag("json"))
        {
            _output.Line(CliJson.Write(devices
                .Select(d => new UsbJson($"{d.VendorId}:{d.ProductId}", d.VendorId, d.ProductId, d.Description))
                .ToArray()));
            return 0;
        }

        if (devices.Count == 0)
        {
            _output.Line($"'{vm.Config.Name}' has no USB passthrough devices.");
            return 0;
        }

        var table = new TextTable("VENDOR:PRODUCT", "DESCRIPTION");
        foreach (UsbPassthroughConfig d in devices)
        {
            table.AddRow($"{d.VendorId}:{d.ProductId}", string.IsNullOrEmpty(d.Description) ? "(unnamed)" : d.Description!);
        }

        _output.Out.Write(table.Render());
        return 0;
    }

    private async Task<int> AddAsync(ParsedArgs args, CancellationToken cancellationToken)
    {
        Vm vm = await ResolveAsync(args, cancellationToken);
        (string vendor, string product) = ParseVendorProduct(args.PositionalOrNull(2));
        string? description = args.Option("description");

        if (vm.Config.UsbDevices.Any(d =>
                string.Equals(d.VendorId, vendor, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(d.ProductId, product, StringComparison.OrdinalIgnoreCase)))
        {
            throw new CliException($"'{vm.Config.Name}' already passes through {vendor}:{product}.");
        }

        var added = new UsbPassthroughConfig { VendorId = vendor, ProductId = product, Description = description };
        VmConfig updated = vm.Config with { UsbDevices = [.. vm.Config.UsbDevices, added] };
        await _repository.SaveAsync(updated, cancellationToken);

        _output.Line($"Added USB passthrough {vendor}:{product} to '{vm.Config.Name}'.");
        await ApplyToRunningVmAsync(vm, args, attach: true, vendor, product, cancellationToken);
        return 0;
    }

    private async Task<int> RemoveAsync(ParsedArgs args, CancellationToken cancellationToken)
    {
        Vm vm = await ResolveAsync(args, cancellationToken);
        (string vendor, string product) = ParseVendorProduct(args.PositionalOrNull(2));

        UsbPassthroughConfig[] remaining = vm.Config.UsbDevices
            .Where(d => !(string.Equals(d.VendorId, vendor, StringComparison.OrdinalIgnoreCase) &&
                          string.Equals(d.ProductId, product, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        if (remaining.Length == vm.Config.UsbDevices.Count)
        {
            throw new CliException($"'{vm.Config.Name}' does not pass through {vendor}:{product}.");
        }

        await _repository.SaveAsync(vm.Config with { UsbDevices = remaining }, cancellationToken);
        _output.Line($"Removed USB passthrough {vendor}:{product} from '{vm.Config.Name}'.");
        await ApplyToRunningVmAsync(vm, args, attach: false, vendor, product, cancellationToken);
        return 0;
    }

    private Task<Vm> ResolveAsync(ParsedArgs args, CancellationToken cancellationToken)
    {
        string reference = args.PositionalOrNull(1)
            ?? throw new CliException($"Usage: boxwright {Usage}");
        return _resolver.ResolveAsync(reference, cancellationToken);
    }

    // After editing the persisted config, optionally apply the change live. Without --now we just note
    // that a running VM picks it up on next boot; with --now we adopt the running VM and hot-plug/unplug
    // via QMP. The adopted handle is intentionally not disposed (disposing clears runtime.json — see
    // DisplayCommand), matching how other live commands re-adopt a running VM.
    private async Task ApplyToRunningVmAsync(Vm vm, ParsedArgs args, bool attach, string vendor, string product, CancellationToken cancellationToken)
    {
        if (!args.HasFlag("now"))
        {
            if (_statusProbe.IsRunning(vm))
            {
                _output.Line("  The VM is running; it takes effect on next boot (pass --now to apply it live).");
            }

            return;
        }

        IRunningVm? running = await _launcher.AdoptAsync(vm, cancellationToken);
        if (running is null)
        {
            _output.Line("  --now had no effect: the VM isn't running. The change applies on next boot.");
            return;
        }

        try
        {
            if (attach)
            {
                await running.AttachUsbAsync(vendor, product, cancellationToken);
                _output.Line("  Attached to the running VM now.");
            }
            else
            {
                await running.DetachUsbAsync(vendor, product, cancellationToken);
                _output.Line("  Detached from the running VM now.");
            }
        }
        catch (QmpCommandException ex)
        {
            throw new CliException($"QEMU rejected the live USB change: {ex.Message}");
        }
    }

    private static (string Vendor, string Product) ParseVendorProduct(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new CliException("A USB device id is required, as vendor:product (e.g. 046d:c52b).");
        }

        string[] parts = value.Split(':');
        if (parts.Length != 2 || !UsbId.IsValid(parts[0].ToLowerInvariant()) || !UsbId.IsValid(parts[1].ToLowerInvariant()))
        {
            throw new CliException($"'{value}' is not a valid USB id. Use vendor:product as four hex digits each, e.g. 046d:c52b.");
        }

        return (parts[0].ToLowerInvariant(), parts[1].ToLowerInvariant());
    }
}

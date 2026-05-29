using Boxwright.Qmp;

namespace Boxwright.Core;

/// <summary>
/// Translates a <see cref="VmConfig"/> (plus the resolved accelerator and the
/// per-launch <see cref="QemuLaunchContext"/>) into a qemu-system-&lt;arch&gt;
/// argument list. This is the single place that owns command-line correctness
/// (ADR-0001); it is a pure function — no process launching — and is golden-tested.
/// </summary>
/// <remarks>
/// File paths (disks, ISOs) are emitted verbatim from the config; the QEMU process
/// is launched with its working directory set to the VM folder (CORE-7), so
/// relative paths resolve there. User-mode (SLIRP) networking is the default and
/// needs no admin (architecture §7).
/// </remarks>
public static class CommandLineBuilder
{
    /// <summary>Builds the ordered QEMU argument list for the given VM.</summary>
    /// <exception cref="ArgumentException">The config uses UEFI but no firmware path was supplied.</exception>
    public static IReadOnlyList<string> Build(VmConfig config, Accelerator accelerator, QemuLaunchContext context)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.QmpEndpoint);

        var args = new List<string>
        {
            "-name", config.Name,
            "-machine", config.Machine,
            "-accel", AccelValue(accelerator),
            "-cpu", config.Cpu.Model,
            "-smp", $"sockets={config.Cpu.Sockets},cores={config.Cpu.Cores},threads={config.Cpu.Threads}",
            "-m", $"{config.MemoryMiB}",
        };

        AppendFirmware(args, config, context);
        AppendDisks(args, config);
        AppendRemovableMedia(args, config);
        AppendNetworking(args, config);
        AppendDisplay(args, config, context);

        args.Add("-qmp");
        args.Add(QmpArgument(context.QmpEndpoint));

        args.Add("-boot");
        args.Add($"order={config.Boot.Order},menu={(config.Boot.Menu ? "on" : "off")}");

        return args;
    }

    // GATE-0: WHPX runs cleaner with the in-kernel IRQ chip disabled.
    private static string AccelValue(Accelerator accelerator) =>
        accelerator == Accelerator.Whpx ? "whpx,kernel-irqchip=off" : accelerator.ToQemuValue();

    private static void AppendFirmware(List<string> args, VmConfig config, QemuLaunchContext context)
    {
        if (!string.Equals(config.Firmware, "uefi", StringComparison.OrdinalIgnoreCase))
        {
            return; // "bios" (default) uses QEMU's built-in firmware; no argument needed.
        }

        if (string.IsNullOrWhiteSpace(context.UefiFirmwarePath))
        {
            throw new ArgumentException(
                "UEFI firmware requires a firmware path (QemuLaunchContext.UefiFirmwarePath).", nameof(context));
        }

        args.Add("-bios");
        args.Add(context.UefiFirmwarePath);
    }

    private static void AppendDisks(List<string> args, VmConfig config)
    {
        foreach (DiskConfig disk in config.Disks)
        {
            args.Add("-drive");
            args.Add($"file={disk.File},format={disk.Format},if={disk.Interface}");
        }
    }

    private static void AppendRemovableMedia(List<string> args, VmConfig config)
    {
        foreach (RemovableMediaConfig media in config.RemovableMedia)
        {
            if (!media.Attached || string.IsNullOrEmpty(media.File))
            {
                continue;
            }

            args.Add("-drive");
            args.Add($"file={media.File},media=cdrom");
        }
    }

    private static void AppendNetworking(List<string> args, VmConfig config)
    {
        // MVP: user-mode (SLIRP) networking — no admin required (architecture §7).
        // Bridged/TAP is a later, gated feature.
        string hostForwards = string.Concat(
            config.Network.PortForwards.Select(f => $",hostfwd=tcp::{f.HostPort}-:{f.GuestPort}"));

        args.Add("-netdev");
        args.Add($"user,id=net0{hostForwards}");
        args.Add("-device");
        args.Add($"{config.Network.Model},netdev=net0");
    }

    private static void AppendDisplay(List<string> args, VmConfig config, QemuLaunchContext context)
    {
        if (string.Equals(config.Display.Protocol, "spice", StringComparison.OrdinalIgnoreCase))
        {
            string spice = $"port={context.SpicePort},addr=127.0.0.1,disable-ticketing=on";
            if (config.Display.Gl)
            {
                spice += ",gl=on";
            }

            args.Add("-spice");
            args.Add(spice);
        }
        else if (string.Equals(config.Display.Protocol, "vnc", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("-vnc");
            args.Add($"127.0.0.1:{context.SpicePort}");
        }
    }

    private static string QmpArgument(QmpEndpoint endpoint) => endpoint.Transport switch
    {
        QmpTransport.Tcp => $"tcp:{endpoint.Host}:{endpoint.Port},server,nowait",
        QmpTransport.Unix => $"unix:{endpoint.SocketPath},server,nowait",
        _ => throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint.Transport, "Unknown QMP transport."),
    };
}

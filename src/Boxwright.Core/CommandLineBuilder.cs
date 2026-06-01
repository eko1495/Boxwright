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
    /// <summary>
    /// The QMP drive id given to the first attached optical medium, so it can be ejected
    /// live (VirtualBox-style) via the <c>eject</c> command while the VM runs — see
    /// <see cref="RunningVm.EjectIsoAsync"/>.
    /// </summary>
    public const string CdromDriveId = "boxwright-cd0";

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
            "-cpu", CpuModel(config, accelerator),
            "-smp", $"sockets={config.Cpu.Sockets},cores={config.Cpu.Cores},threads={config.Cpu.Threads}",
            "-m", $"{config.MemoryMiB}",
        };

        AppendFirmware(args, config, context);
        AppendDisks(args, config);
        AppendRemovableMedia(args, config);
        AppendNetworking(args, config);
        AppendInput(args);
        AppendDisplay(args, config, context);
        AppendGuestChannels(args, config, context);

        args.Add("-qmp");
        args.Add(QmpArgument(context.QmpEndpoint));

        args.Add("-boot");
        args.Add($"order={config.Boot.Order},menu={(config.Boot.Menu ? "on" : "off")}");

        return args;
    }

    // GATE-0: WHPX runs cleaner with the in-kernel IRQ chip disabled.
    private static string AccelValue(Accelerator accelerator) =>
        accelerator == Accelerator.Whpx ? "whpx,kernel-irqchip=off" : accelerator.ToQemuValue();

    // The host-passthrough CPU models ("host"/"max") abort under WHPX with
    // "Unexpected VP exit code 4" — they expose features (APX/MPX, …) WHPX cannot run.
    // Substitute a broadly-compatible, x86-64-v2 named model so modern guests (e.g.
    // Ubuntu 24.04, which requires x86-64-v2) still boot. KVM/HVF keep the configured
    // model, where "host" gives near-native performance. An explicit model is honoured
    // as-is. Verified by the e2e dry-run on Windows: host/max fail; named models work.
    private const string WhpxCompatibleCpu = "Westmere";

    private static string CpuModel(VmConfig config, Accelerator accelerator) =>
        accelerator == Accelerator.Whpx && config.Cpu.Model is "host" or "max"
            ? WhpxCompatibleCpu
            : config.Cpu.Model;

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
        int index = 0;
        foreach (RemovableMediaConfig media in config.RemovableMedia)
        {
            if (!media.Attached || string.IsNullOrEmpty(media.File))
            {
                continue;
            }

            // A stable drive id so the medium can be ejected live via QMP (the install-finish
            // "remove the installation medium" step). The first cdrom uses CdromDriveId, which
            // is what the live eject targets; any extras get distinct ids to avoid collisions.
            string id = index == 0 ? CdromDriveId : $"boxwright-cd{index}";
            args.Add("-drive");
            args.Add($"file={media.File},media=cdrom,id={id}");
            index++;
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

    private static void AppendInput(List<string> args)
    {
        // An absolute pointing device so the guest cursor tracks the host pointer 1:1.
        // Without it QEMU falls back to a relative PS/2 mouse that desyncs in the viewer,
        // so clicks land in the wrong place (GATE-1 dry-run: couldn't click the installer).
        args.Add("-usb");
        args.Add("-device");
        args.Add("usb-tablet");
    }

    private static void AppendDisplay(List<string> args, VmConfig config, QemuLaunchContext context)
    {
        // A real GPU so the guest desktop renders. QEMU's bare "std" VGA commonly leaves
        // modern GNOME/Wayland on a black screen; QXL pairs with SPICE (and works under VNC).
        args.Add("-vga");
        args.Add("qxl");

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
            // QEMU's -vnc takes a *display number* (listen port = 5900 + display), not a raw
            // port. Convert the allocated port so QEMU actually listens on it.
            args.Add("-vnc");
            args.Add($"127.0.0.1:{context.SpicePort - 5900}");
        }
    }

    // Agent channels share one virtio-serial bus: the QEMU Guest Agent (clean shutdown +
    // guest IP, always wired) and, for SPICE displays, the spice-vdagent (clipboard +
    // auto-resize). Each needs its in-guest agent installed; the channels are harmless when
    // absent. The guest-agent channel is TCP on loopback — uniform across OSes, like QMP on Windows.
    private static void AppendGuestChannels(List<string> args, VmConfig config, QemuLaunchContext context)
    {
        args.Add("-device");
        args.Add("virtio-serial-pci");

        args.Add("-chardev");
        args.Add($"socket,host=127.0.0.1,port={context.GuestAgentPort},server=on,wait=off,id=qga0");
        args.Add("-device");
        args.Add("virtserialport,chardev=qga0,name=org.qemu.guest_agent.0");

        if (string.Equals(config.Display.Protocol, "spice", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("-chardev");
            args.Add("spicevmc,id=spicechannel0,name=vdagent");
            args.Add("-device");
            args.Add("virtserialport,chardev=spicechannel0,name=com.redhat.spice.0");
        }
    }

    private static string QmpArgument(QmpEndpoint endpoint) => endpoint.Transport switch
    {
        QmpTransport.Tcp => $"tcp:{endpoint.Host}:{endpoint.Port},server,nowait",
        QmpTransport.Unix => $"unix:{endpoint.SocketPath},server,nowait",
        _ => throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint.Transport, "Unknown QMP transport."),
    };
}

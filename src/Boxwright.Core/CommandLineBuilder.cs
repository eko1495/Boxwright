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
        if (IsWindows(config))
        {
            // Windows Setup has no in-box virtio driver, so it can't see a virtio disk. Put storage on
            // the q35 chipset's built-in AHCI/SATA controller (in-box AHCI driver), so Setup sees the
            // disk and the install/autounattend media with no extra drivers (see ADR-0015).
            AppendWindowsStorage(args, config);
        }
        else
        {
            AppendDisks(args, config);
            AppendRemovableMedia(args, config);
        }

        AppendNetworking(args, config);
        AppendInput(args);
        AppendDisplay(args, config, context);
        AppendAudio(args, config);
        AppendGuestChannels(args, config, context);

        args.Add("-qmp");
        args.Add(QmpArgument(context.QmpEndpoint));

        AppendInstallBoot(args, config);

        args.Add("-boot");
        args.Add($"order={config.Boot.Order},menu={(config.Boot.Menu ? "on" : "off")}");

        return args;
    }

    // One-shot direct-kernel boot for an unattended ISO install (ADR-0013 Phase B). The installer ISO and
    // the CIDATA seed stay attached as normal devices (above); -kernel just overrides what boots, and the
    // 'autoinstall' token in the append is what makes the installer run non-interactively. Cleared after
    // the install finishes (the guest powers off), so later boots come up off the installed disk.
    private static void AppendInstallBoot(List<string> args, VmConfig config)
    {
        if (config.InstallBoot is not { } install)
        {
            return;
        }

        args.Add("-kernel");
        args.Add(install.KernelFile);
        args.Add("-initrd");
        args.Add(install.InitrdFile);
        args.Add("-append");
        args.Add(install.Append);
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

        if (string.IsNullOrWhiteSpace(context.UefiCodePath) || string.IsNullOrWhiteSpace(context.UefiVarsPath))
        {
            throw new ArgumentException(
                "UEFI firmware requires resolved CODE and VARS paths (QemuLaunchContext.UefiCodePath/UefiVarsPath).", nameof(context));
        }

        // Split OVMF: read-only firmware CODE (unit 0) + a writable per-VM VARS/NVRAM (unit 1), via pflash.
        args.Add("-drive");
        args.Add($"if=pflash,format=raw,unit=0,readonly=on,file={context.UefiCodePath}");
        args.Add("-drive");
        args.Add($"if=pflash,format=raw,unit=1,file={context.UefiVarsPath}");
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

    private static bool IsWindows(VmConfig config) =>
        string.Equals(config.OsType, "windows", StringComparison.OrdinalIgnoreCase);

    // Windows storage on q35's built-in AHCI: each disk is an ide-hd and each attached optical medium an
    // ide-cd, on its own SATA port (ide.0, ide.1, …). Drives use if=none + an explicit device so they
    // land on the AHCI bus (Windows has an in-box AHCI driver; it has none for virtio). The first cdrom
    // keeps CdromDriveId so the live-eject (RunningVm.EjectIsoAsync) still finds it.
    private static void AppendWindowsStorage(List<string> args, VmConfig config)
    {
        int port = 0;

        int diskIndex = 0;
        foreach (DiskConfig disk in config.Disks)
        {
            string id = $"hd{diskIndex}";
            args.Add("-drive");
            args.Add($"if=none,id={id},file={disk.File},format={disk.Format}");
            args.Add("-device");
            args.Add($"ide-hd,drive={id},bus=ide.{port}");
            port++;
            diskIndex++;
        }

        int cdIndex = 0;
        foreach (RemovableMediaConfig media in config.RemovableMedia)
        {
            if (!media.Attached || string.IsNullOrEmpty(media.File))
            {
                continue;
            }

            string id = cdIndex == 0 ? CdromDriveId : $"boxwright-cd{cdIndex}";
            args.Add("-drive");
            args.Add($"if=none,id={id},file={media.File},format=raw,media=cdrom");
            args.Add("-device");
            args.Add($"ide-cd,drive={id},bus=ide.{port}");
            port++;
            cdIndex++;
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
        bool isVnc = string.Equals(config.Display.Protocol, "vnc", StringComparison.OrdinalIgnoreCase);

        // Per-guest GPU (mirrors Quickemu): Windows → qxl (good in-box Windows drivers), macOS →
        // vmware-svga, everything else (Linux) → virtio-gpu, which renders modern GNOME/Wayland
        // (Ubuntu 24.04+, Fedora) where qxl black-screens it. VNC always uses virtio-gpu — it's
        // efficient and VGA-compatible at boot, so it renders before guest drivers load.
        args.Add("-vga");
        args.Add(isVnc ? "virtio" : VgaForGuest(config.OsType));

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
        else if (isVnc)
        {
            // QEMU's -vnc takes a *display number* (listen port = 5900 + display), not a raw
            // port. Convert the allocated port so QEMU actually listens on it.
            args.Add("-vnc");
            args.Add($"127.0.0.1:{context.SpicePort - 5900}");
        }
    }

    // A sound card (Intel HD Audio) so the guest has audio. The backend rides the display: SPICE
    // plays audio over the SPICE connection (remote-viewer), so there's no host-audio-driver
    // dependency and nothing that can break VM launch. VNC has no audio channel in our embedded
    // client, so it gets the null backend (a silent card) for now. Disabled → no card at all.
    private static void AppendAudio(List<string> args, VmConfig config)
    {
        if (!config.Audio.Enabled)
        {
            return;
        }

        bool isVnc = string.Equals(config.Display.Protocol, "vnc", StringComparison.OrdinalIgnoreCase);
        args.Add("-audiodev");
        args.Add($"{(isVnc ? "none" : "spice")},id=audio0");
        args.Add("-device");
        args.Add("intel-hda");
        args.Add("-device");
        args.Add("hda-duplex,audiodev=audio0");
    }

    // The QEMU "-vga" device for a guest OS over SPICE (VNC always uses virtio-gpu). Mirrors
    // Quickemu's per-guest choice; an unknown OsType falls back to the Linux/virtio default.
    private static string VgaForGuest(string osType) => osType.ToLowerInvariant() switch
    {
        "windows" => "qxl",   // qxl-vga
        "macos" => "vmware",  // vmware-svga
        _ => "virtio",        // virtio-vga (Linux + default)
    };

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

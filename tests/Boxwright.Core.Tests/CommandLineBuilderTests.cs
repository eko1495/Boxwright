using Boxwright.Qmp;
using Xunit;

namespace Boxwright.Core.Tests;

// CORE-5: CommandLineBuilder (config -> qemu args). Golden-file style.
public class CommandLineBuilderTests
{
    private static VmConfig CanonicalConfig() => new()
    {
        Name = "Test VM",
        Machine = "q35",
        Firmware = "bios",
        Cpu = new CpuConfig { Model = "host", Sockets = 1, Cores = 4, Threads = 1 },
        MemoryMiB = 4096,
        Disks = [new DiskConfig { File = "disk.qcow2", Format = "qcow2", Interface = "virtio" }],
        RemovableMedia = [new RemovableMediaConfig { Type = "cdrom", File = "ubuntu.iso", Attached = true }],
        Network = new NetworkConfig
        {
            Mode = "user",
            Model = "virtio-net",
            PortForwards = [new PortForward { HostPort = 2222, GuestPort = 22 }],
        },
        Display = new DisplayConfig { Protocol = "spice", Gl = false },
        Boot = new BootConfig { Order = "cd", Menu = false },
    };

    private static QemuLaunchContext TcpContext(int spicePort = 5930) => new()
    {
        QmpEndpoint = QmpEndpoint.Tcp("127.0.0.1", 4444),
        SpicePort = spicePort,
    };

    [Fact]
    public void Build_CanonicalBiosVm_ProducesExpectedArgs()
    {
        IReadOnlyList<string> args = CommandLineBuilder.Build(CanonicalConfig(), Accelerator.Tcg, TcpContext());

        string[] expected =
        [
            "-name", "Test VM",
            "-machine", "q35",
            "-accel", "tcg",
            "-cpu", "host",
            "-smp", "sockets=1,cores=4,threads=1",
            "-m", "4096",
            "-drive", "file=disk.qcow2,format=qcow2,if=virtio",
            "-drive", "file=ubuntu.iso,media=cdrom,id=boxwright-cd0",
            "-netdev", "user,id=net0,hostfwd=tcp::2222-:22",
            "-device", "virtio-net,netdev=net0",
            "-usb",
            "-device", "usb-tablet",
            "-vga", "qxl",
            "-spice", "port=5930,addr=127.0.0.1,disable-ticketing=on",
            "-qmp", "tcp:127.0.0.1:4444,server,nowait",
            "-boot", "order=cd,menu=off",
        ];
        Assert.Equal(expected, args);
    }

    [Fact]
    public void Build_IncludesUsbTablet_ForAbsolutePointer()
    {
        IReadOnlyList<string> args = CommandLineBuilder.Build(CanonicalConfig(), Accelerator.Tcg, TcpContext());

        Assert.Contains("-usb", args);
        Assert.Contains("usb-tablet", args);
    }

    [Fact]
    public void Build_IncludesQxlGpu_SoTheDesktopRenders()
    {
        IReadOnlyList<string> args = CommandLineBuilder.Build(CanonicalConfig(), Accelerator.Tcg, TcpContext());

        Assert.Equal("qxl", ArgValue(args, "-vga"));
    }

    [Fact]
    public void Build_VncProtocol_UsesDisplayNumberNotRawPort()
    {
        // QEMU's -vnc takes a display number (listen port = 5900 + display); the allocated
        // port 5930 is display 30. The old code passed the raw port and never bound.
        VmConfig config = CanonicalConfig() with { Display = new DisplayConfig { Protocol = "vnc" } };

        IReadOnlyList<string> args = CommandLineBuilder.Build(config, Accelerator.Tcg, TcpContext(5930));

        Assert.Equal("127.0.0.1:30", ArgValue(args, "-vnc"));
        Assert.DoesNotContain("-spice", args);
    }

    [Fact]
    public void Build_Whpx_UsesKernelIrqchipOff()
    {
        IReadOnlyList<string> args = CommandLineBuilder.Build(CanonicalConfig(), Accelerator.Whpx, TcpContext());

        Assert.Equal("whpx,kernel-irqchip=off", ArgValue(args, "-accel"));
    }

    [Fact]
    public void Build_Kvm_UsesPlainKvm()
    {
        IReadOnlyList<string> args = CommandLineBuilder.Build(CanonicalConfig(), Accelerator.Kvm, TcpContext());

        Assert.Equal("kvm", ArgValue(args, "-accel"));
    }

    [Fact]
    public void Build_WhpxWithHostCpu_SubstitutesCompatibleModel()
    {
        // WHPX cannot run "-cpu host"/"max" (they abort with "Unexpected VP exit code 4").
        IReadOnlyList<string> args = CommandLineBuilder.Build(CanonicalConfig(), Accelerator.Whpx, TcpContext());

        Assert.Equal("Westmere", ArgValue(args, "-cpu"));
    }

    [Fact]
    public void Build_KvmWithHostCpu_KeepsHostPassthrough()
    {
        // KVM (and HVF) handle "-cpu host" natively — keep it for near-native performance.
        IReadOnlyList<string> args = CommandLineBuilder.Build(CanonicalConfig(), Accelerator.Kvm, TcpContext());

        Assert.Equal("host", ArgValue(args, "-cpu"));
    }

    [Fact]
    public void Build_WhpxWithExplicitCpu_KeepsIt()
    {
        // Only the "host"/"max" sentinels are substituted; an explicit model is honoured.
        VmConfig config = CanonicalConfig() with
        {
            Cpu = new CpuConfig { Model = "Haswell", Sockets = 1, Cores = 2, Threads = 1 },
        };

        IReadOnlyList<string> args = CommandLineBuilder.Build(config, Accelerator.Whpx, TcpContext());

        Assert.Equal("Haswell", ArgValue(args, "-cpu"));
    }

    [Fact]
    public void Build_Uefi_WithFirmwarePath_EmitsBios()
    {
        VmConfig config = CanonicalConfig() with { Firmware = "uefi" };
        var context = new QemuLaunchContext
        {
            QmpEndpoint = QmpEndpoint.Tcp("127.0.0.1", 4444),
            SpicePort = 5930,
            UefiFirmwarePath = "/firmware/OVMF.fd",
        };

        IReadOnlyList<string> args = CommandLineBuilder.Build(config, Accelerator.Tcg, context);

        Assert.Equal("/firmware/OVMF.fd", ArgValue(args, "-bios"));
    }

    [Fact]
    public void Build_Uefi_WithoutFirmwarePath_Throws()
    {
        VmConfig config = CanonicalConfig() with { Firmware = "uefi" };

        Assert.Throws<ArgumentException>(() => CommandLineBuilder.Build(config, Accelerator.Tcg, TcpContext()));
    }

    [Fact]
    public void Build_NoPortForwards_EmitsPlainUserNetdev()
    {
        VmConfig config = CanonicalConfig() with
        {
            Network = new NetworkConfig { Mode = "user", Model = "virtio-net", PortForwards = [] },
        };

        IReadOnlyList<string> args = CommandLineBuilder.Build(config, Accelerator.Tcg, TcpContext());

        Assert.Equal("user,id=net0", ArgValue(args, "-netdev"));
    }

    [Fact]
    public void Build_UnixQmpEndpoint_EmitsUnixSocketArg()
    {
        var context = new QemuLaunchContext
        {
            QmpEndpoint = QmpEndpoint.UnixSocket("/tmp/qmp.sock"),
            SpicePort = 5930,
        };

        IReadOnlyList<string> args = CommandLineBuilder.Build(CanonicalConfig(), Accelerator.Tcg, context);

        Assert.Equal("unix:/tmp/qmp.sock,server,nowait", ArgValue(args, "-qmp"));
    }

    [Fact]
    public void Build_DetachedRemovableMedia_IsOmitted()
    {
        VmConfig config = CanonicalConfig() with
        {
            RemovableMedia = [new RemovableMediaConfig { Type = "cdrom", File = "ubuntu.iso", Attached = false }],
        };

        IReadOnlyList<string> args = CommandLineBuilder.Build(config, Accelerator.Tcg, TcpContext());

        Assert.DoesNotContain("file=ubuntu.iso,media=cdrom", args);
    }

    private static string ArgValue(IReadOnlyList<string> args, string flag)
    {
        for (int i = 0; i < args.Count - 1; i++)
        {
            if (args[i] == flag)
            {
                return args[i + 1];
            }
        }

        throw new InvalidOperationException($"Flag '{flag}' not found in args.");
    }
}

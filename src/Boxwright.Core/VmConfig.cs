namespace Boxwright.Core;

/// <summary>
/// A single VM's configuration — the contents of its JSON config file (see
/// docs/architecture.md §9 and ADR-0006). These are boot-time settings only;
/// runtime control happens over QMP. Edit with <c>with</c> expressions and
/// persist via <see cref="VmConfigJson"/>.
/// </summary>
public sealed record VmConfig
{
    /// <summary>The config schema version this build reads and writes.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Versions the config format so migrations stay explicit.</summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>Stable unique id for the VM (typically a GUID string).</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Human-friendly VM name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Guest architecture, e.g. <c>x86_64</c>.</summary>
    public string Arch { get; init; } = "x86_64";

    /// <summary>QEMU machine type, e.g. <c>q35</c>.</summary>
    public string Machine { get; init; } = "q35";

    /// <summary>Firmware: <c>bios</c> or <c>uefi</c>.</summary>
    public string Firmware { get; init; } = "uefi";

    /// <summary>CPU model and topology.</summary>
    public CpuConfig Cpu { get; init; } = new();

    /// <summary>Guest RAM in MiB.</summary>
    public int MemoryMiB { get; init; } = 2048;

    /// <summary>Fixed disks attached to the VM.</summary>
    public IReadOnlyList<DiskConfig> Disks { get; init; } = [];

    /// <summary>Removable media (CD-ROM/ISO) slots.</summary>
    public IReadOnlyList<RemovableMediaConfig> RemovableMedia { get; init; } = [];

    /// <summary>Networking configuration.</summary>
    public NetworkConfig Network { get; init; } = new();

    /// <summary>Display/console configuration.</summary>
    public DisplayConfig Display { get; init; } = new();

    /// <summary>
    /// Accelerator selection. Persisted as <c>auto</c> and resolved per-host at
    /// launch; never persisted as a concrete value like <c>kvm</c> (ADR-0003).
    /// </summary>
    public string Accelerator { get; init; } = "auto";

    /// <summary>Boot order and menu options.</summary>
    public BootConfig Boot { get; init; } = new();
}

/// <summary>CPU model and topology.</summary>
public sealed record CpuConfig
{
    /// <summary>CPU model, e.g. <c>host</c>.</summary>
    public string Model { get; init; } = "host";

    /// <summary>Number of sockets.</summary>
    public int Sockets { get; init; } = 1;

    /// <summary>Cores per socket.</summary>
    public int Cores { get; init; } = 2;

    /// <summary>Threads per core.</summary>
    public int Threads { get; init; } = 1;
}

/// <summary>A fixed disk image.</summary>
public sealed record DiskConfig
{
    /// <summary>Disk image file name (relative to the VM folder) or path.</summary>
    public string File { get; init; } = string.Empty;

    /// <summary>Image format, e.g. <c>qcow2</c>.</summary>
    public string Format { get; init; } = "qcow2";

    /// <summary>Disk interface, e.g. <c>virtio</c>.</summary>
    public string Interface { get; init; } = "virtio";
}

/// <summary>A removable-media slot (e.g. a CD-ROM holding an ISO).</summary>
public sealed record RemovableMediaConfig
{
    /// <summary>Media type, e.g. <c>cdrom</c>.</summary>
    public string Type { get; init; } = "cdrom";

    /// <summary>Backing file (ISO) path, or null for an empty slot.</summary>
    public string? File { get; init; }

    /// <summary>Whether the media is currently attached.</summary>
    public bool Attached { get; init; }
}

/// <summary>Networking configuration. Defaults to user-mode (SLIRP), which needs no admin (architecture §7).</summary>
public sealed record NetworkConfig
{
    /// <summary>Network mode, e.g. <c>user</c> (SLIRP) or <c>tap</c>.</summary>
    public string Mode { get; init; } = "user";

    /// <summary>NIC model, e.g. <c>virtio-net</c>.</summary>
    public string Model { get; init; } = "virtio-net";

    /// <summary>Host-to-guest port forwards over user-mode networking.</summary>
    public IReadOnlyList<PortForward> PortForwards { get; init; } = [];
}

/// <summary>A host:guest port forward over user-mode networking.</summary>
public sealed record PortForward
{
    /// <summary>Host-side port.</summary>
    public int HostPort { get; init; }

    /// <summary>Guest-side port.</summary>
    public int GuestPort { get; init; }
}

/// <summary>Display/console configuration.</summary>
public sealed record DisplayConfig
{
    /// <summary>Display protocol, e.g. <c>spice</c>.</summary>
    public string Protocol { get; init; } = "spice";

    /// <summary>Whether OpenGL acceleration is requested.</summary>
    public bool Gl { get; init; }
}

/// <summary>Boot order and firmware menu options.</summary>
public sealed record BootConfig
{
    /// <summary>Boot order string, e.g. <c>cd</c> (CD then disk).</summary>
    public string Order { get; init; } = "cd";

    /// <summary>Whether to show the firmware boot menu.</summary>
    public bool Menu { get; init; }
}

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

    /// <summary>Firmware: <c>bios</c> (the MVP default — simplest first boot) or <c>uefi</c> (OVMF firmware located by <see cref="QemuLocator.ResolveUefiFirmware"/>, with a per-VM NVRAM copy).</summary>
    public string Firmware { get; init; } = "bios";

    /// <summary>
    /// Guest OS family — <c>linux</c> (default), <c>windows</c>, or <c>macos</c>. Selects a
    /// guest-appropriate virtual GPU (Linux → virtio-gpu, Windows → qxl, macOS → vmware-svga),
    /// mirroring Quickemu. Boot-time only.
    /// </summary>
    public string OsType { get; init; } = "linux";

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

    /// <summary>Audio/sound-card configuration.</summary>
    public AudioConfig Audio { get; init; } = new();

    /// <summary>
    /// Accelerator selection. Persisted as <c>auto</c> and resolved per-host at
    /// launch; never persisted as a concrete value like <c>kvm</c> (ADR-0003).
    /// </summary>
    public string Accelerator { get; init; } = "auto";

    /// <summary>Boot order and menu options.</summary>
    public BootConfig Boot { get; init; } = new();

    /// <summary>
    /// Direct-kernel boot for a one-shot unattended install (ADR-0013 Phase B). When non-null, the VM
    /// boots the extracted installer kernel/initrd with an <c>autoinstall</c> command line so the install
    /// runs hands-free; it is cleared (back to null) once the install finishes and the guest powers off,
    /// after which the VM boots the installed disk normally. Null for an ordinary VM.
    /// </summary>
    public InstallBootConfig? InstallBoot { get; init; }

    /// <summary>
    /// True while a from-scratch unattended <b>Windows</b> install is in progress (ADR-0015). The Windows
    /// analogue of <see cref="InstallBoot"/>: while set, the app auto-presses a key at boot to start Windows
    /// Setup from the CD ("Press any key to boot from CD…"), and once Setup finishes and the guest powers
    /// off (the Autounattend's final shutdown) the install media is ejected and boot switches to the disk.
    /// False for an ordinary VM.
    /// </summary>
    public bool WindowsInstallInProgress { get; init; }
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

/// <summary>Audio/sound-card configuration.</summary>
public sealed record AudioConfig
{
    /// <summary>Whether the VM has a sound card (Intel HD Audio). On by default.</summary>
    public bool Enabled { get; init; } = true;
}

/// <summary>Boot order and firmware menu options.</summary>
public sealed record BootConfig
{
    /// <summary>Boot order string, e.g. <c>cd</c> (CD then disk).</summary>
    public string Order { get; init; } = "cd";

    /// <summary>Whether to show the firmware boot menu.</summary>
    public bool Menu { get; init; }
}

/// <summary>
/// A one-shot direct-kernel boot for an unattended ISO install (ADR-0013 Phase B): QEMU boots the kernel
/// and initrd extracted from the installer ISO with <see cref="Append"/> on the kernel command line, which
/// is what makes the installer (e.g. Ubuntu subiquity) run fully non-interactively. File names are relative
/// to the VM folder (QEMU's working directory). See <see cref="InstallMediaExtractor"/>.
/// </summary>
public sealed record InstallBootConfig
{
    /// <summary>Extracted kernel image file name (relative to the VM folder), e.g. <c>vmlinuz</c>.</summary>
    public string KernelFile { get; init; } = string.Empty;

    /// <summary>Extracted initrd file name (relative to the VM folder), e.g. <c>initrd</c>.</summary>
    public string InitrdFile { get; init; } = string.Empty;

    /// <summary>The kernel command line, e.g. <c>autoinstall ds=nocloud layerfs-path=…</c>.</summary>
    public string Append { get; init; } = string.Empty;
}

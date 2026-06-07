namespace Boxwright.Core;

/// <summary>
/// The pinned <b>virtio-win</b> driver ISO (Red Hat / Fedora) used by the optional Windows
/// performance path (ADR-0018): it carries the signed Windows drivers for QEMU's paravirtualized
/// devices, so Windows Setup can see a <c>virtio-blk</c> disk and the installed OS gets
/// <c>virtio-net</c>. Distributed as a single ISO; Boxwright downloads + caches it via the existing
/// <see cref="IIsoDownloader"/> (a synthetic <see cref="CatalogEntry"/>). The pin is best-effort and
/// rotates with upstream — refreshable like the OS catalog entries.
/// </summary>
public static class VirtioWin
{
    /// <summary>Pinned upstream version.</summary>
    public const string Version = "0.1.285";

    /// <summary>Direct download URL of the pinned versioned ISO.</summary>
    public static readonly Uri IsoUrl = new(
        "https://fedorapeople.org/groups/virt/virtio-win/direct-downloads/archive-virtio/virtio-win-0.1.285-1/virtio-win-0.1.285.iso");

    /// <summary>Expected SHA-256 (lowercase hex). Upstream publishes none for the ISO, so this is hashed once and pinned.</summary>
    public const string Sha256 = "e14cf2b94492c3e925f0070ba7fdfedeb2048c91eea9c5a5afb30232a3976331";

    /// <summary>Expected download size in bytes (progress + cache-hit detection).</summary>
    public const long SizeBytes = 789645312;

    // The virtio-win ISO lays drivers out as \<driver>\<windows-folder>\<arch>\ (e.g. \viostor\w11\amd64).
    /// <summary>virtio-blk storage driver folder (must load in WinPE so Setup sees the disk).</summary>
    public const string StorageDriver = "viostor";

    /// <summary>virtio-net network driver folder.</summary>
    public const string NetworkDriver = "NetKVM";

    /// <summary>The per-Windows-version driver subfolder this build targets (Windows 11).</summary>
    public const string WindowsFolder = "w11";

    /// <summary>The driver architecture subfolder.</summary>
    public const string Arch = "amd64";

    /// <summary>A synthetic catalog entry so <see cref="IIsoDownloader.EnsureAsync"/> downloads/verifies/caches the ISO.</summary>
    public static OsCatalogEntry CatalogEntry => new()
    {
        Id = $"virtio-win-{Version}",
        Name = "virtio-win drivers",
        Version = Version,
        IsoUrl = IsoUrl,
        Sha256 = Sha256,
        SizeBytes = SizeBytes,
        SourceName = "Fedora · fedorapeople.org",
    };
}

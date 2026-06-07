namespace Boxwright.Core;

/// <summary>
/// The per-VM record of external/live snapshots, persisted beside <c>vm.json</c> as
/// <c>live-snapshots.json</c> (ADR-0021). It is the source of truth for user-facing names and
/// timestamps; the qcow2 backing chain on disk is the source of truth for structure (revert/delete read
/// the actual backing pointers, never this file).
/// </summary>
public sealed record LiveSnapshotManifest
{
    /// <summary>The manifest schema version this build reads and writes.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Versions the manifest format so migrations stay explicit.</summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>The recorded live snapshots, oldest first.</summary>
    public IReadOnlyList<LiveSnapshotEntry> Snapshots { get; init; } = [];
}

/// <summary>One live snapshot: a named, timestamped point-in-time across the VM's qcow2 disks.</summary>
public sealed record LiveSnapshotEntry
{
    /// <summary>Stable short identifier (also embedded in the on-disk file names).</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>User-facing name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>When the snapshot was taken (UTC).</summary>
    public DateTimeOffset CreatedUtc { get; init; }

    /// <summary>The per-disk frozen (read-only) point-in-time files this snapshot captured.</summary>
    public IReadOnlyList<LiveSnapshotDisk> Disks { get; init; } = [];
}

/// <summary>The frozen file a live snapshot captured for one disk.</summary>
public sealed record LiveSnapshotDisk
{
    /// <summary>Index of the disk in <see cref="VmConfig.Disks"/>.</summary>
    public int DiskIndex { get; init; }

    /// <summary>The frozen (now read-only) image file, relative to the VM folder. Revert layers a fresh overlay over this.</summary>
    public string FrozenFile { get; init; } = string.Empty;
}

/// <summary>
/// A request to snapshot one running disk: the disk's current active image (absolute path, used to resolve
/// the live block device) and the absolute path of the overlay QEMU should switch writes into.
/// </summary>
public sealed record LiveSnapshotDiskRequest(string ActiveFilePath, string OverlayFilePath);

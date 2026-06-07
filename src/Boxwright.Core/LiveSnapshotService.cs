namespace Boxwright.Core;

/// <summary>
/// Orchestrates external/live snapshots (ADR-0021): the live "take" runs over QMP through the supplied
/// <see cref="IRunningVm"/>; revert/delete are stopped-time qcow2 operations via <see cref="IDiskService"/>.
/// Snapshot names/timestamps live in a <c>live-snapshots.json</c> sidecar; structural decisions
/// (revert/delete) read the actual qcow2 backing pointers, never the sidecar.
/// </summary>
public sealed class LiveSnapshotService : ILiveSnapshotService
{
    /// <summary>The live-snapshot sidecar file name inside each VM folder.</summary>
    public const string ManifestFileName = "live-snapshots.json";

    private readonly IDiskService _diskService;
    private readonly VmRepository _repository;

    /// <summary>Creates a live-snapshot service.</summary>
    public LiveSnapshotService(IDiskService diskService, VmRepository repository)
    {
        ArgumentNullException.ThrowIfNull(diskService);
        ArgumentNullException.ThrowIfNull(repository);
        _diskService = diskService;
        _repository = repository;
    }

    /// <inheritdoc />
    public async Task<Vm> TakeAsync(Vm vm, IRunningVm session, string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        IReadOnlyList<(DiskConfig Disk, int Index)> qcow2Disks = Qcow2Disks(vm);
        if (qcow2Disks.Count == 0)
        {
            throw new DiskException("This VM has no qcow2 disk to snapshot.");
        }

        string id = NewId();
        var requests = new List<LiveSnapshotDiskRequest>(qcow2Disks.Count);
        var frozen = new List<LiveSnapshotDisk>(qcow2Disks.Count);
        var newFiles = new Dictionary<int, string>(qcow2Disks.Count);
        foreach ((DiskConfig disk, int index) in qcow2Disks)
        {
            string activeAbs = Path.Combine(vm.FolderPath, disk.File);
            string overlayName = OverlayFileName(index, id);
            requests.Add(new LiveSnapshotDiskRequest(activeAbs, Path.Combine(vm.FolderPath, overlayName)));
            frozen.Add(new LiveSnapshotDisk { DiskIndex = index, FrozenFile = disk.File });
            newFiles[index] = overlayName;
        }

        // Live, atomic across all disks. After this, the guest writes into the overlays.
        await session.TakeLiveSnapshotAsync(requests, cancellationToken);

        // CRITICAL: repoint the config to the new overlays. Without this, the next cold boot would mount the
        // frozen base read-write — losing post-snapshot writes and corrupting the snapshot. vm.json first.
        Vm updated = RepointDisks(vm, newFiles);
        await _repository.SaveAsync(updated.Config, cancellationToken);

        // Record the snapshot (names/timestamps) after the boot-critical config write.
        string manifestPath = ManifestPath(vm);
        LiveSnapshotManifest manifest = await LiveSnapshotManifestJson.LoadAsync(manifestPath, cancellationToken);
        var entry = new LiveSnapshotEntry
        {
            Id = id,
            Name = name,
            CreatedUtc = DateTimeOffset.UtcNow,
            Disks = frozen,
        };
        LiveSnapshotManifest appended = manifest with { Snapshots = [.. manifest.Snapshots, entry] };
        await LiveSnapshotManifestJson.SaveAsync(manifestPath, appended, cancellationToken);

        return updated;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LiveSnapshotEntry>> ListAsync(Vm vm, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vm);
        LiveSnapshotManifest manifest = await LiveSnapshotManifestJson.LoadAsync(ManifestPath(vm), cancellationToken);
        return manifest.Snapshots;
    }

    /// <inheritdoc />
    public async Task<Vm> RevertAsync(Vm vm, string snapshotId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotId);

        LiveSnapshotManifest manifest = await LiveSnapshotManifestJson.LoadAsync(ManifestPath(vm), cancellationToken);
        LiveSnapshotEntry entry = FindEntry(manifest, snapshotId);

        string newId = NewId();
        var newFiles = new Dictionary<int, string>(entry.Disks.Count);
        foreach (LiveSnapshotDisk disk in entry.Disks)
        {
            string frozenAbs = Path.Combine(vm.FolderPath, disk.FrozenFile);

            // INVARIANT: never point the disk directly at a frozen file — it is a read-only backing. Always
            // layer a fresh overlay over it (qemu-img create -b), so the snapshot stays immutable.
            string overlayName = OverlayFileName(disk.DiskIndex, newId);
            await _diskService.CreateOverlayAsync(frozenAbs, Path.Combine(vm.FolderPath, overlayName), cancellationToken);
            newFiles[disk.DiskIndex] = overlayName;
        }

        Vm updated = RepointDisks(vm, newFiles);
        await _repository.SaveAsync(updated.Config, cancellationToken);
        return updated;
    }

    /// <inheritdoc />
    public async Task<Vm> DeleteAsync(Vm vm, string snapshotId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotId);

        LiveSnapshotManifest manifest = await LiveSnapshotManifestJson.LoadAsync(ManifestPath(vm), cancellationToken);
        LiveSnapshotEntry entry = FindEntry(manifest, snapshotId);

        foreach (LiveSnapshotDisk disk in entry.Disks)
        {
            string frozenAbs = Path.GetFullPath(Path.Combine(vm.FolderPath, disk.FrozenFile));
            if (!File.Exists(frozenAbs))
            {
                continue; // already gone; just prune the entry below
            }

            // The parent the dependents must be folded onto = this frozen file's own backing.
            DiskInfo info = await _diskService.GetInfoAsync(frozenAbs, cancellationToken);
            string? parentAbs = info.FullBackingFilename;

            IReadOnlyList<string> children = await FindChildrenAsync(vm.FolderPath, frozenAbs, cancellationToken);
            if (children.Count > 0 && parentAbs is null)
            {
                // The frozen file is a full base image (no backing). Folding dependents in would require a
                // flatten/compact pass — deferred (ADR-0021). Refuse rather than risk it.
                throw new DiskException(
                    $"Can't delete snapshot '{entry.Name}': it is a base image with no parent to fold its dependents into.");
            }

            // Rebase EVERY dependent off the frozen file first (safe mode preserves content); delete it LAST,
            // so a mid-operation failure never leaves a dependent pointing at a removed backing.
            foreach (string childAbs in children)
            {
                await _diskService.RebaseAsync(childAbs, parentAbs!, cancellationToken);
            }

            File.Delete(frozenAbs);
        }

        LiveSnapshotManifest pruned = manifest with
        {
            Snapshots = [.. manifest.Snapshots.Where(s => !string.Equals(s.Id, snapshotId, StringComparison.Ordinal))],
        };
        await LiveSnapshotManifestJson.SaveAsync(ManifestPath(vm), pruned, cancellationToken);
        return vm; // config unchanged — only backing pointers moved, file names are stable
    }

    // qcow2 files in the VM folder whose immediate backing is targetAbs (full-path compared).
    private async Task<IReadOnlyList<string>> FindChildrenAsync(string folderPath, string targetAbs, CancellationToken cancellationToken)
    {
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var children = new List<string>();
        foreach (string candidate in Directory.EnumerateFiles(folderPath, "*.qcow2"))
        {
            string candidateAbs = Path.GetFullPath(candidate);
            if (string.Equals(candidateAbs, targetAbs, comparison))
            {
                continue; // the frozen file itself
            }

            DiskInfo info = await _diskService.GetInfoAsync(candidateAbs, cancellationToken);
            if (info.FullBackingFilename is { } backing
                && string.Equals(Path.GetFullPath(backing), targetAbs, comparison))
            {
                children.Add(candidateAbs);
            }
        }

        return children;
    }

    private static IReadOnlyList<(DiskConfig Disk, int Index)> Qcow2Disks(Vm vm) =>
        [.. vm.Config.Disks
            .Select((disk, index) => (Disk: disk, Index: index))
            .Where(x => string.Equals(x.Disk.Format, "qcow2", StringComparison.OrdinalIgnoreCase))];

    private static Vm RepointDisks(Vm vm, Dictionary<int, string> newFiles)
    {
        var disks = vm.Config.Disks
            .Select((disk, index) => newFiles.TryGetValue(index, out string? file) ? disk with { File = file } : disk)
            .ToList();
        return vm with { Config = vm.Config with { Disks = disks } };
    }

    private static LiveSnapshotEntry FindEntry(LiveSnapshotManifest manifest, string snapshotId) =>
        manifest.Snapshots.FirstOrDefault(s => string.Equals(s.Id, snapshotId, StringComparison.Ordinal))
            ?? throw new DiskException($"No live snapshot with id '{snapshotId}'.");

    private static string ManifestPath(Vm vm) => Path.Combine(vm.FolderPath, ManifestFileName);

    private static string OverlayFileName(int diskIndex, string id) => $"disk{diskIndex}-{id}.qcow2";

    private static string NewId() => Guid.NewGuid().ToString("N")[..8];
}

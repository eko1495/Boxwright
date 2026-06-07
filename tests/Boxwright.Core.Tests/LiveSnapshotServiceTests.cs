using Boxwright.Core;
using Xunit;

namespace Boxwright.Core.Tests;

public sealed class LiveSnapshotServiceTests
{
    [Fact]
    public async Task TakeAsync_RepointsConfigToOverlay_AndRecordsFrozenFile()
    {
        using var temp = new TempVm(disk: "disk.qcow2");
        var disk = new FakeDiskService();
        var service = new LiveSnapshotService(disk, temp.Repository);
        var session = new FakeRunningVm(temp.Folder);

        Vm updated = await service.TakeAsync(temp.Vm, session, "before-update");

        // Config repointed to a fresh overlay (never left on the frozen base).
        string newFile = updated.Config.Disks[0].File;
        Assert.StartsWith("disk0-", newFile);
        Assert.EndsWith(".qcow2", newFile);
        Assert.NotEqual("disk.qcow2", newFile);

        // The QMP take targeted the active image and the new overlay.
        (string Active, string Overlay) request = Assert.Single(session.Requests);
        Assert.Equal(Path.Combine(temp.Folder, "disk.qcow2"), request.Active);
        Assert.Equal(Path.Combine(temp.Folder, newFile), request.Overlay);

        // vm.json on disk reflects the repoint (boot-critical).
        VmConfig persisted = await VmConfigJson.LoadAsync(temp.Vm.ConfigPath);
        Assert.Equal(newFile, persisted.Disks[0].File);

        // The sidecar records the snapshot, frozen at the previously-active file.
        LiveSnapshotManifest manifest = await LiveSnapshotManifestJson.LoadAsync(temp.ManifestPath);
        LiveSnapshotEntry entry = Assert.Single(manifest.Snapshots);
        Assert.Equal("before-update", entry.Name);
        Assert.Equal("disk.qcow2", Assert.Single(entry.Disks).FrozenFile);
    }

    [Fact]
    public async Task TakeAsync_NoQcow2Disk_Throws()
    {
        using var temp = new TempVm(disk: "disk.raw", format: "raw");
        var service = new LiveSnapshotService(new FakeDiskService(), temp.Repository);

        await Assert.ThrowsAsync<DiskException>(() =>
            service.TakeAsync(temp.Vm, new FakeRunningVm(temp.Folder), "x"));
    }

    [Fact]
    public async Task RevertAsync_LayersOverlayOverFrozenFile_NeverPointsAtFrozen()
    {
        using var temp = new TempVm(disk: "disk-live.qcow2"); // current live overlay
        await temp.WriteManifestAsync(Entry("s1", (0, "disk.qcow2")));
        var disk = new FakeDiskService();
        var service = new LiveSnapshotService(disk, temp.Repository);

        Vm updated = await service.RevertAsync(temp.Vm, "s1");

        (string Backing, string Overlay) overlay = Assert.Single(disk.Overlays);
        Assert.Equal(Path.Combine(temp.Folder, "disk.qcow2"), overlay.Backing); // backed by the frozen file
        string newFile = updated.Config.Disks[0].File;
        Assert.Equal(Path.Combine(temp.Folder, newFile), overlay.Overlay);
        Assert.NotEqual("disk.qcow2", newFile); // INVARIANT: disk never points at a read-only backing
        Assert.StartsWith("disk0-", newFile);
    }

    [Fact]
    public async Task DeleteAsync_RebasesEveryChildOntoParent_ThenDeletesFrozenFile()
    {
        using var temp = new TempVm(disk: "live.qcow2");
        temp.Touch("base.qcow2", "x.qcow2", "live.qcow2");
        await temp.WriteManifestAsync(Entry("s1", (0, "x.qcow2")));
        var disk = new FakeDiskService();
        disk.Backing[temp.Abs("x.qcow2")] = temp.Abs("base.qcow2");
        disk.Backing[temp.Abs("live.qcow2")] = temp.Abs("x.qcow2");
        disk.Backing[temp.Abs("base.qcow2")] = null;
        var service = new LiveSnapshotService(disk, temp.Repository);

        await service.DeleteAsync(temp.Vm, "s1");

        (string Image, string NewBacking) rebase = Assert.Single(disk.Rebases);
        Assert.Equal(temp.Abs("live.qcow2"), rebase.Image);
        Assert.Equal(temp.Abs("base.qcow2"), rebase.NewBacking);
        Assert.False(File.Exists(temp.Abs("x.qcow2"))); // frozen file removed last
        LiveSnapshotManifest manifest = await LiveSnapshotManifestJson.LoadAsync(temp.ManifestPath);
        Assert.Empty(manifest.Snapshots);
    }

    [Fact]
    public async Task DeleteAsync_AbortsBeforeDeleting_WhenARebaseFails()
    {
        using var temp = new TempVm(disk: "live.qcow2");
        temp.Touch("base.qcow2", "x.qcow2", "live.qcow2");
        await temp.WriteManifestAsync(Entry("s1", (0, "x.qcow2")));
        var disk = new FakeDiskService { RebaseThrows = true };
        disk.Backing[temp.Abs("x.qcow2")] = temp.Abs("base.qcow2");
        disk.Backing[temp.Abs("live.qcow2")] = temp.Abs("x.qcow2");
        disk.Backing[temp.Abs("base.qcow2")] = null;
        var service = new LiveSnapshotService(disk, temp.Repository);

        await Assert.ThrowsAsync<DiskException>(() => service.DeleteAsync(temp.Vm, "s1"));

        Assert.True(File.Exists(temp.Abs("x.qcow2"))); // not deleted — abort before delete
        LiveSnapshotManifest manifest = await LiveSnapshotManifestJson.LoadAsync(temp.ManifestPath);
        Assert.Single(manifest.Snapshots); // entry still present
    }

    [Fact]
    public async Task DeleteAsync_BaseImageWithDependents_Throws()
    {
        using var temp = new TempVm(disk: "x.qcow2");
        temp.Touch("base.qcow2", "x.qcow2");
        await temp.WriteManifestAsync(Entry("s1", (0, "base.qcow2")));
        var disk = new FakeDiskService();
        disk.Backing[temp.Abs("base.qcow2")] = null;            // base has no parent
        disk.Backing[temp.Abs("x.qcow2")] = temp.Abs("base.qcow2"); // but something depends on it
        var service = new LiveSnapshotService(disk, temp.Repository);

        await Assert.ThrowsAsync<DiskException>(() => service.DeleteAsync(temp.Vm, "s1"));
        Assert.True(File.Exists(temp.Abs("base.qcow2")));
    }

    [Fact]
    public async Task ListAsync_ReturnsManifestEntries()
    {
        using var temp = new TempVm(disk: "disk.qcow2");
        await temp.WriteManifestAsync(Entry("s1", (0, "disk.qcow2")), Entry("s2", (0, "disk0-s1.qcow2")));
        var service = new LiveSnapshotService(new FakeDiskService(), temp.Repository);

        IReadOnlyList<LiveSnapshotEntry> entries = await service.ListAsync(temp.Vm);

        Assert.Equal(["s1", "s2"], entries.Select(e => e.Id));
    }

    private static LiveSnapshotEntry Entry(string id, params (int Index, string Frozen)[] disks) => new()
    {
        Id = id,
        Name = id,
        CreatedUtc = DateTimeOffset.UnixEpoch,
        Disks = [.. disks.Select(d => new LiveSnapshotDisk { DiskIndex = d.Index, FrozenFile = d.Frozen })],
    };

    // ---- test doubles ----

    private sealed class TempVm : IDisposable
    {
        public TempVm(string disk, string format = "qcow2")
        {
            string root = Path.Combine(Path.GetTempPath(), "bw-live-" + Guid.NewGuid().ToString("N"));
            Repository = new VmRepository(root);
            string id = Guid.NewGuid().ToString();
            var config = new VmConfig { Id = id, Name = "Test", Disks = [new DiskConfig { File = disk, Format = format }] };
            Folder = Path.Combine(root, id);
            Directory.CreateDirectory(Folder);
            VmConfigJson.SaveAsync(Path.Combine(Folder, VmRepository.ConfigFileName), config).GetAwaiter().GetResult();
            File.WriteAllText(Path.Combine(Folder, disk), "img");
            Vm = new Vm(Folder, config);
            _root = root;
        }

        private readonly string _root;

        public VmRepository Repository { get; }

        public string Folder { get; }

        public Vm Vm { get; }

        public string ManifestPath => Path.Combine(Folder, LiveSnapshotService.ManifestFileName);

        public string Abs(string fileName) => Path.GetFullPath(Path.Combine(Folder, fileName));

        public void Touch(params string[] fileNames)
        {
            foreach (string name in fileNames)
            {
                File.WriteAllText(Path.Combine(Folder, name), "img");
            }
        }

        public Task WriteManifestAsync(params LiveSnapshotEntry[] entries) =>
            LiveSnapshotManifestJson.SaveAsync(ManifestPath, new LiveSnapshotManifest { Snapshots = entries });

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private sealed class FakeDiskService : IDiskService
    {
        public Dictionary<string, string?> Backing { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<(string Backing, string Overlay)> Overlays { get; } = [];

        public List<(string Image, string NewBacking)> Rebases { get; } = [];

        public bool RebaseThrows { get; init; }

        public Task CreateAsync(string path, long sizeBytes, string format = "qcow2", CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<DiskInfo> GetInfoAsync(string path, CancellationToken cancellationToken = default)
        {
            string key = Path.GetFullPath(path);
            string? backing = Backing.TryGetValue(key, out string? b) ? b : null;
            return Task.FromResult(new DiskInfo { Filename = path, Format = "qcow2", FullBackingFilename = backing });
        }

        public Task CopyAsync(string sourcePath, string destinationPath, string format = "qcow2", CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ResizeAsync(string path, long sizeBytes, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task CreateOverlayAsync(string backingPath, string overlayPath, CancellationToken cancellationToken = default)
        {
            Overlays.Add((backingPath, overlayPath));
            File.WriteAllText(overlayPath, "overlay");
            return Task.CompletedTask;
        }

        public Task RebaseAsync(string imagePath, string newBackingPath, CancellationToken cancellationToken = default)
        {
            if (RebaseThrows)
            {
                throw new DiskException("rebase failed");
            }

            Rebases.Add((Path.GetFullPath(imagePath), Path.GetFullPath(newBackingPath)));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRunningVm(string folder) : IRunningVm
    {
        public List<(string Active, string Overlay)> Requests { get; } = [];

        public Accelerator Accelerator => default;

        public int SpicePort => 0;

        public string DisplayProtocol => "spice";

        public QemuProcessState State => default;

        public event EventHandler? Exited { add { } remove { } }

        public Task TakeLiveSnapshotAsync(IReadOnlyList<LiveSnapshotDiskRequest> disks, CancellationToken cancellationToken = default)
        {
            foreach (LiveSnapshotDiskRequest disk in disks)
            {
                Requests.Add((disk.ActiveFilePath, disk.OverlayFilePath));
                File.WriteAllText(disk.OverlayFilePath, "overlay"); // QEMU would create the overlay
            }

            _ = folder;
            return Task.CompletedTask;
        }

        public Task RequestShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ResumeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ResetAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task EjectIsoAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveStateAsync(string tag, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task LoadStateAsync(string tag, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteStateAsync(string tag, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<string>> GetGuestAddressesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<VmMetricsSample> GetMetricsSampleAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(default(VmMetricsSample));

        public Task SendKeyEventAsync(string qcode, bool down, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public void ForceStop()
        {
        }

        public Task StopAsync(TimeSpan gracePeriod, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

using Xunit;

namespace Boxwright.Core.Tests;

// VmSnapshotService spans the per-disk ISnapshotService across all of a VM's qcow2 disks.
// Driven by a recording fake ISnapshotService (no qemu-img).
public sealed class VmSnapshotServiceTests
{
    private static Vm VmWith(params DiskConfig[] disks) =>
        new("/vms/vm1", new VmConfig { Name = "vm1", Disks = disks });

    private static DiskConfig Qcow2(string file) => new() { File = file, Format = "qcow2" };

    [Fact]
    public async Task CreateAsync_snapshots_every_qcow2_disk()
    {
        var fake = new FakeSnapshotService();
        var service = new VmSnapshotService(fake);

        await service.CreateAsync(VmWith(Qcow2("os.qcow2"), Qcow2("data.qcow2")), "v1");

        Assert.Equal(
            [(Path.Combine("/vms/vm1", "os.qcow2"), "v1"), (Path.Combine("/vms/vm1", "data.qcow2"), "v1")],
            fake.Created);
    }

    [Fact]
    public async Task CreateAsync_skips_non_qcow2_disks()
    {
        var fake = new FakeSnapshotService();
        var service = new VmSnapshotService(fake);

        await service.CreateAsync(
            VmWith(Qcow2("os.qcow2"), new DiskConfig { File = "seed.img", Format = "raw" }),
            "v1");

        (string disk, string tag) = Assert.Single(fake.Created);
        Assert.Equal(Path.Combine("/vms/vm1", "os.qcow2"), disk);
        Assert.Equal("v1", tag);
    }

    [Fact]
    public async Task CreateAsync_rolls_back_earlier_disks_when_a_later_one_fails()
    {
        var fake = new FakeSnapshotService { FailCreateOn = Path.Combine("/vms/vm1", "data.qcow2") };
        var service = new VmSnapshotService(fake);

        await Assert.ThrowsAsync<DiskException>(() =>
            service.CreateAsync(VmWith(Qcow2("os.qcow2"), Qcow2("data.qcow2")), "v1"));

        // The first disk's snapshot was undone, so no half-created tag survives.
        (string disk, string tag) = Assert.Single(fake.Deleted);
        Assert.Equal(Path.Combine("/vms/vm1", "os.qcow2"), disk);
        Assert.Equal("v1", tag);
    }

    [Fact]
    public async Task RestoreAsync_restores_every_disk_when_the_tag_is_complete()
    {
        var fake = new FakeSnapshotService();
        fake.SetSnapshots(Path.Combine("/vms/vm1", "os.qcow2"), "v1");
        fake.SetSnapshots(Path.Combine("/vms/vm1", "data.qcow2"), "v1");
        var service = new VmSnapshotService(fake);

        await service.RestoreAsync(VmWith(Qcow2("os.qcow2"), Qcow2("data.qcow2")), "v1");

        Assert.Equal(
            [(Path.Combine("/vms/vm1", "os.qcow2"), "v1"), (Path.Combine("/vms/vm1", "data.qcow2"), "v1")],
            fake.Restored);
    }

    [Fact]
    public async Task RestoreAsync_refuses_and_touches_nothing_when_the_tag_is_missing_on_a_disk()
    {
        var fake = new FakeSnapshotService();
        fake.SetSnapshots(Path.Combine("/vms/vm1", "os.qcow2"), "v1");
        fake.SetSnapshots(Path.Combine("/vms/vm1", "data.qcow2")); // no v1 here
        var service = new VmSnapshotService(fake);

        DiskException ex = await Assert.ThrowsAsync<DiskException>(() =>
            service.RestoreAsync(VmWith(Qcow2("os.qcow2"), Qcow2("data.qcow2")), "v1"));

        Assert.Contains("incomplete", ex.Message, StringComparison.Ordinal);
        Assert.Empty(fake.Restored); // validated up-front, before any disk was restored
    }

    [Fact]
    public async Task DeleteAsync_deletes_from_disks_that_have_the_tag_and_skips_the_rest()
    {
        var fake = new FakeSnapshotService();
        fake.SetSnapshots(Path.Combine("/vms/vm1", "os.qcow2"), "v1");
        fake.SetSnapshots(Path.Combine("/vms/vm1", "data.qcow2")); // tag absent → skipped
        var service = new VmSnapshotService(fake);

        await service.DeleteAsync(VmWith(Qcow2("os.qcow2"), Qcow2("data.qcow2")), "v1");

        (string disk, string tag) = Assert.Single(fake.Deleted);
        Assert.Equal(Path.Combine("/vms/vm1", "os.qcow2"), disk);
        Assert.Equal("v1", tag);
    }

    [Fact]
    public async Task ListAsync_returns_only_snapshots_present_on_every_disk()
    {
        var fake = new FakeSnapshotService();
        fake.SetSnapshots(Path.Combine("/vms/vm1", "os.qcow2"), "complete", "os-only");
        fake.SetSnapshots(Path.Combine("/vms/vm1", "data.qcow2"), "complete");
        var service = new VmSnapshotService(fake);

        IReadOnlyList<VmSnapshot> snaps = await service.ListAsync(VmWith(Qcow2("os.qcow2"), Qcow2("data.qcow2")));

        VmSnapshot snap = Assert.Single(snaps);
        Assert.Equal("complete", snap.Name); // "os-only" is missing from data.qcow2, so it's hidden
    }

    [Fact]
    public async Task ListAsync_on_a_single_disk_returns_its_snapshots_verbatim()
    {
        var fake = new FakeSnapshotService();
        fake.SetSnapshots(Path.Combine("/vms/vm1", "os.qcow2"), "a", "b");
        var service = new VmSnapshotService(fake);

        IReadOnlyList<VmSnapshot> snaps = await service.ListAsync(VmWith(Qcow2("os.qcow2")));

        Assert.Equal(["a", "b"], snaps.Select(s => s.Name));
    }

    [Fact]
    public async Task NoQcow2Disk_throws()
    {
        var service = new VmSnapshotService(new FakeSnapshotService());

        await Assert.ThrowsAsync<DiskException>(() =>
            service.CreateAsync(VmWith(new DiskConfig { File = "seed.img", Format = "raw" }), "v1"));
    }

    private sealed class FakeSnapshotService : ISnapshotService
    {
        private readonly Dictionary<string, List<string>> _byDisk = new(StringComparer.Ordinal);

        public List<(string Disk, string Tag)> Created { get; } = [];

        public List<(string Disk, string Tag)> Restored { get; } = [];

        public List<(string Disk, string Tag)> Deleted { get; } = [];

        public string? FailCreateOn { get; init; }

        public void SetSnapshots(string disk, params string[] tags) => _byDisk[disk] = [.. tags];

        public Task<IReadOnlyList<VmSnapshot>> ListAsync(string diskPath, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<VmSnapshot> snaps = _byDisk.TryGetValue(diskPath, out List<string>? tags)
                ? [.. tags.Select(t => new VmSnapshot { Name = t })]
                : [];
            return Task.FromResult(snaps);
        }

        public Task CreateAsync(string diskPath, string tag, CancellationToken cancellationToken = default)
        {
            if (string.Equals(diskPath, FailCreateOn, StringComparison.Ordinal))
            {
                throw new DiskException($"qemu-img snapshot -c failed on {diskPath}");
            }

            Created.Add((diskPath, tag));
            return Task.CompletedTask;
        }

        public Task RestoreAsync(string diskPath, string tag, CancellationToken cancellationToken = default)
        {
            Restored.Add((diskPath, tag));
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string diskPath, string tag, CancellationToken cancellationToken = default)
        {
            Deleted.Add((diskPath, tag));
            return Task.CompletedTask;
        }
    }
}

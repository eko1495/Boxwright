using Xunit;

namespace Boxwright.Core.Tests;

// VmDeletionService refuses to delete a VM that backs a linked clone (ADR-0025), detected via the
// clones' qcow2 backing pointers. Uses a real VmRepository over a temp dir + a fake disk service that
// reports each disk's backing file (no qemu-img).
public sealed class VmDeletionServiceTests : IDisposable
{
    private readonly string _root;
    private readonly VmRepository _repository;
    private readonly FakeDiskService _disks = new();

    public VmDeletionServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"boxwright-del-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _repository = new VmRepository(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private Task<Vm> NewVmAsync(string name, string format = "qcow2") => _repository.CreateAsync(new VmConfig
    {
        Name = name,
        Disks = [new DiskConfig { File = "disk.qcow2", Format = format, Interface = "virtio" }],
    });

    private static string DiskOf(Vm vm) => Path.Combine(vm.FolderPath, "disk.qcow2");

    [Fact]
    public async Task Delete_refuses_when_a_linked_clone_is_backed_by_the_vm()
    {
        Vm template = await NewVmAsync("template");
        Vm clone = await NewVmAsync("instance");
        _disks.Backing[DiskOf(clone)] = DiskOf(template); // the clone's overlay is backed by the template's disk
        var service = new VmDeletionService(_repository, _disks);

        VmHasDependentsException ex = await Assert.ThrowsAsync<VmHasDependentsException>(() =>
            service.DeleteAsync(template));

        Assert.Contains("instance", ex.Message, StringComparison.Ordinal);
        Assert.Equal(["instance"], ex.DependentNames);
        Assert.Contains(await _repository.ListAsync(), v => v.Config.Id == template.Config.Id); // not deleted
    }

    [Fact]
    public async Task Delete_succeeds_when_nothing_depends_on_the_vm()
    {
        Vm template = await NewVmAsync("template");
        await NewVmAsync("unrelated"); // independent: no backing file
        var service = new VmDeletionService(_repository, _disks);

        await service.DeleteAsync(template);

        Assert.DoesNotContain(await _repository.ListAsync(), v => v.Config.Id == template.Config.Id);
    }

    [Fact]
    public async Task Delete_allows_removing_the_clone_itself()
    {
        Vm template = await NewVmAsync("template");
        Vm clone = await NewVmAsync("instance");
        _disks.Backing[DiskOf(clone)] = DiskOf(template);
        var service = new VmDeletionService(_repository, _disks);

        // Nothing is backed by the clone, so deleting the clone (not the template) is fine.
        await service.DeleteAsync(clone);

        Assert.DoesNotContain(await _repository.ListAsync(), v => v.Config.Id == clone.Config.Id);
    }

    [Fact]
    public async Task FindDependents_lists_the_linked_clones()
    {
        Vm template = await NewVmAsync("template");
        Vm cloneA = await NewVmAsync("a");
        Vm cloneB = await NewVmAsync("b");
        _disks.Backing[DiskOf(cloneA)] = DiskOf(template);
        _disks.Backing[DiskOf(cloneB)] = DiskOf(template);
        var service = new VmDeletionService(_repository, _disks);

        IReadOnlyList<Vm> dependents = await service.FindDependentsAsync(template);

        Assert.Equal(["a", "b"], dependents.Select(d => d.Config.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_backing_file_outside_the_vms_folder_is_not_a_dependency()
    {
        Vm template = await NewVmAsync("template");
        Vm other = await NewVmAsync("other");
        _disks.Backing[DiskOf(other)] = Path.Combine(_root, "some-shared-base", "base.qcow2"); // not template's folder
        var service = new VmDeletionService(_repository, _disks);

        await service.DeleteAsync(template); // allowed

        Assert.Empty(await service.FindDependentsAsync(other));
    }

    [Fact]
    public async Task Non_qcow2_and_unreadable_disks_are_ignored()
    {
        Vm template = await NewVmAsync("template");
        await NewVmAsync("raw-disk-vm", format: "raw"); // raw disks carry no backing → skipped without a qemu-img call
        Vm broken = await NewVmAsync("broken");
        _disks.ThrowFor.Add(DiskOf(broken)); // qemu-img can't read it → skipped, doesn't block the delete
        var service = new VmDeletionService(_repository, _disks);

        await service.DeleteAsync(template);

        Assert.DoesNotContain(await _repository.ListAsync(), v => v.Config.Id == template.Config.Id);
    }

    private sealed class FakeDiskService : IDiskService
    {
        public Dictionary<string, string> Backing { get; } = new(StringComparer.Ordinal);

        public HashSet<string> ThrowFor { get; } = new(StringComparer.Ordinal);

        public Task<DiskCheckResult> CheckAsync(string path, DiskRepairMode repair = DiskRepairMode.None, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DiskInfo> GetInfoAsync(string path, CancellationToken cancellationToken = default)
        {
            if (ThrowFor.Contains(path))
            {
                throw new DiskException($"qemu-img info failed for {path}");
            }

            return Task.FromResult(new DiskInfo
            {
                FullBackingFilename = Backing.TryGetValue(path, out string? backing) ? backing : null,
            });
        }

        public Task CreateAsync(string path, long sizeBytes, string format = "qcow2", CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ResizeAsync(string path, long sizeBytes, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task CopyAsync(string sourcePath, string destinationPath, string format = "qcow2", CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task CreateOverlayAsync(string backingPath, string overlayPath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RebaseAsync(string imagePath, string newBackingPath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

using Xunit;

namespace Boxwright.Core.Tests;

// VmRenameService reslugs a VM's folder (ADR-0028) while keeping the id as the stable key. Guarded:
// refuses a linked-clone source (absolute backing paths would break) and a running VM. Real VmRepository
// over a temp dir + a fake disk service (no qemu-img) + an in-memory runtime store.
public sealed class VmRenameServiceTests : IDisposable
{
    private readonly string _root;
    private readonly VmRepository _repository;
    private readonly FakeDiskService _disks = new();
    private readonly FakeRuntimeStore _runtime = new();
    private readonly VmRenameService _service;

    public VmRenameServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"boxwright-rename-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _repository = new VmRepository(_root);
        _service = new VmRenameService(_repository, new VmDeletionService(_repository, _disks), _runtime);
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

    private static string FolderName(Vm vm) => Path.GetFileName(vm.FolderPath.TrimEnd(Path.DirectorySeparatorChar));

    [Fact]
    public void ComputeSlug_sanitizes_to_kebab_with_id_suffix()
    {
        // Spaces, mixed case, and Windows-invalid chars collapse to lowercase kebab; the 8-hex id suffix is appended.
        string slug = _service.ComputeSlug("Ubuntu Dev: 24.04!", [], "1a2b3c4d-0000-0000-0000-000000000000");

        Assert.Equal("ubuntu-dev-24-04-1a2b3c4d", slug);
    }

    [Fact]
    public void ComputeSlug_handles_a_reserved_device_name()
    {
        // A VM literally named "CON" must not produce a folder Windows reserves for the console device.
        string slug = _service.ComputeSlug("CON", [], "deadbeef-1111-2222-3333-444444444444");

        Assert.Equal("vm-con-deadbeef", slug);
    }

    [Fact]
    public void ComputeSlug_falls_back_to_id_when_name_has_nothing_sluggable()
    {
        string slug = _service.ComputeSlug("***", [], "abcdef12-0000-0000-0000-000000000000");

        Assert.Equal("abcdef12", slug);
    }

    [Fact]
    public void ComputeSlug_dedupes_a_collision_with_a_numeric_suffix()
    {
        string id = "1a2b3c4d-0000-0000-0000-000000000000";
        string taken = "ubuntu-1a2b3c4d";

        // Case-insensitive match on Windows; the taken name forces a -2 suffix everywhere.
        string slug = _service.ComputeSlug("ubuntu", [taken], id);

        Assert.Equal("ubuntu-1a2b3c4d-2", slug);
    }

    [Fact]
    public async Task RenameAsync_moves_the_folder_updates_the_name_and_keeps_the_id()
    {
        Vm vm = await NewVmAsync("old-name");
        string oldFolder = vm.FolderPath;
        await File.WriteAllTextAsync(DiskOf(vm), "disk-bytes"); // a real disk file rides along with the move

        Vm renamed = await _service.RenameAsync(vm, "Shiny New Name");

        Assert.False(Directory.Exists(oldFolder)); // old GUID folder is gone
        Assert.True(Directory.Exists(renamed.FolderPath)); // slug folder exists
        Assert.StartsWith("shiny-new-name-", FolderName(renamed), StringComparison.Ordinal);
        Assert.True(File.Exists(renamed.ConfigPath)); // vm.json moved with it
        Assert.Equal("disk-bytes", await File.ReadAllTextAsync(DiskOf(renamed))); // the disk moved too
        Assert.Equal("Shiny New Name", renamed.Config.Name);
        Assert.Equal(vm.Config.Id, renamed.Config.Id); // id is the immutable key

        // The VM still resolves by id, now from its slug folder.
        IReadOnlyList<Vm> all = await _repository.ListAsync();
        Vm reloaded = Assert.Single(all);
        Assert.Equal(vm.Config.Id, reloaded.Config.Id);
        Assert.Equal("Shiny New Name", reloaded.Config.Name);
    }

    [Fact]
    public async Task RenameAsync_refuses_when_a_linked_clone_is_backed_by_the_vm()
    {
        Vm template = await NewVmAsync("template");
        Vm clone = await NewVmAsync("instance");
        _disks.Backing[DiskOf(clone)] = DiskOf(template); // the clone's overlay is backed by the source folder
        string templateFolder = template.FolderPath;

        VmHasDependentsException ex = await Assert.ThrowsAsync<VmHasDependentsException>(() =>
            _service.RenameAsync(template, "renamed-template"));

        Assert.Contains("instance", ex.Message, StringComparison.Ordinal);
        Assert.True(Directory.Exists(templateFolder)); // folder NOT moved
    }

    [Fact]
    public async Task RenameAsync_refuses_when_runtime_state_is_present()
    {
        Vm vm = await NewVmAsync("running");
        _runtime.Save(vm, new VmRuntimeState { ProcessId = 4242 });
        string folder = vm.FolderPath;

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RenameAsync(vm, "new-name"));

        Assert.True(Directory.Exists(folder)); // folder unchanged
        Assert.Equal("running", (await _repository.ListAsync())[0].Config.Name); // name unchanged too
    }

    [Fact]
    public async Task RenameAsync_to_the_same_effective_slug_is_a_name_only_update()
    {
        Vm vm = await NewVmAsync("old-name");
        Vm renamed = await _service.RenameAsync(vm, "Real Name"); // now in slug folder real-name-<id8>
        string slugFolder = renamed.FolderPath;

        // Renaming again to the same effective name keeps the existing folder (no move), only re-asserts the name.
        Vm again = await _service.RenameAsync(renamed, "Real Name");

        Assert.Equal(slugFolder, again.FolderPath);
        Assert.Equal("Real Name", again.Config.Name);
        Assert.True(Directory.Exists(slugFolder));
    }

    // An in-memory IVmRuntimeStore: enough to exercise the "is there runtime state?" guard without touching disk.
    private sealed class FakeRuntimeStore : IVmRuntimeStore
    {
        private readonly Dictionary<string, VmRuntimeState> _states = new(StringComparer.Ordinal);

        public void Save(Vm vm, VmRuntimeState state) => _states[vm.Config.Id] = state;

        public VmRuntimeState? TryLoad(Vm vm) => _states.GetValueOrDefault(vm.Config.Id);

        public void Clear(Vm vm) => _states.Remove(vm.Config.Id);
    }

    private sealed class FakeDiskService : IDiskService
    {
        public Dictionary<string, string> Backing { get; } = new(StringComparer.Ordinal);

        public Task<DiskCheckResult> CheckAsync(string path, DiskRepairMode repair = DiskRepairMode.None, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DiskInfo> GetInfoAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DiskInfo
            {
                FullBackingFilename = Backing.TryGetValue(path, out string? backing) ? backing : null,
            });

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

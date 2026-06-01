using Boxwright.App.ViewModels;
using Boxwright.Core;
using Xunit;

namespace Boxwright.App.Tests;

/// <summary>
/// Exercises <see cref="VmListViewModel"/> against a real <see cref="VmRepository"/>
/// over a throwaway temp directory, with fake launcher/dispatcher doubles.
/// </summary>
public sealed class VmListViewModelTests : IDisposable
{
    private readonly string _root;
    private readonly ImmediateUiDispatcher _dispatcher = new();
    private readonly FakeFilePicker _filePicker = new();
    private readonly FakeDisplayLauncher _display = new();
    private readonly FakeLogReader _logReader = new();
    private readonly FakeSnapshotService _snapshots = new();
    private readonly FakeVmCloneService _clone = new();

    public VmListViewModelTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "boxwright-app-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
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

    private VmRepository NewRepository() => new(_root);

    private VmListViewModel NewSut(VmRepository repository) =>
        new(repository, new FakeVmLauncher(new FakeRunningVm()), _dispatcher, _filePicker, _display, _logReader, _snapshots, _clone);

    [Fact]
    public async Task Refresh_WithNoVms_LeavesListEmptyAndFlagsEmpty()
    {
        var sut = NewSut(NewRepository());

        await sut.RefreshCommand.ExecuteAsync(null);

        Assert.Empty(sut.Vms);
        Assert.True(sut.IsEmpty);
        Assert.False(sut.IsLoading);
        Assert.False(sut.HasError);
    }

    [Fact]
    public async Task HasRunningVms_ReflectsLiveSessions_AndShutDownAllStopsThem()
    {
        VmRepository repo = NewRepository();
        await repo.CreateAsync(new VmConfig { Name = "vm1" });
        var sut = NewSut(repo);
        await sut.RefreshCommand.ExecuteAsync(null);
        Assert.False(sut.HasRunningVms);

        await sut.Vms.Single().StartCommand.ExecuteAsync(null);
        Assert.True(sut.HasRunningVms);
        Assert.Single(sut.RunningVms);

        await sut.ShutDownAllAsync();

        Assert.False(sut.HasRunningVms);
        Assert.Empty(sut.RunningVms);
    }

    [Fact]
    public async Task CloningAnItem_AddsTheCloneToTheList()
    {
        VmRepository repo = NewRepository();
        await repo.CreateAsync(new VmConfig
        {
            Name = "base",
            Disks = [new DiskConfig { File = "disk.qcow2", Format = "qcow2", Interface = "virtio" }],
        });
        var sut = NewSut(repo);
        await sut.RefreshCommand.ExecuteAsync(null);
        VmListItemViewModel item = sut.Vms.Single();

        item.CloneName = "copy";
        await item.FullCloneCommand.ExecuteAsync(null);

        Assert.Equal(2, sut.Vms.Count);
        Assert.Contains(sut.Vms, v => v.Name == "copy");
    }

    [Fact]
    public async Task Refresh_LoadsSavedVms_SortedByNameCaseInsensitively()
    {
        VmRepository repo = NewRepository();
        await repo.CreateAsync(new VmConfig { Name = "ubuntu" });
        await repo.CreateAsync(new VmConfig { Name = "Alpine" });
        await repo.CreateAsync(new VmConfig { Name = "debian" });
        var sut = NewSut(repo);

        await sut.RefreshCommand.ExecuteAsync(null);

        Assert.False(sut.IsEmpty);
        Assert.Collection(
            sut.Vms,
            first => Assert.Equal("Alpine", first.Name),
            second => Assert.Equal("debian", second.Name),
            third => Assert.Equal("ubuntu", third.Name));
    }

    [Fact]
    public async Task Refresh_CalledTwice_DoesNotDuplicateItems()
    {
        VmRepository repo = NewRepository();
        await repo.CreateAsync(new VmConfig { Name = "Solo" });
        var sut = NewSut(repo);

        await sut.RefreshCommand.ExecuteAsync(null);
        await sut.RefreshCommand.ExecuteAsync(null);

        Assert.Single(sut.Vms);
    }

    [Fact]
    public async Task SelectedVm_DefaultsToNull_AndTracksAssignment()
    {
        VmRepository repo = NewRepository();
        await repo.CreateAsync(new VmConfig { Name = "Pick me" });
        var sut = NewSut(repo);
        await sut.RefreshCommand.ExecuteAsync(null);

        Assert.Null(sut.SelectedVm);
        Assert.False(sut.HasSelection);

        sut.SelectedVm = sut.Vms[0];

        Assert.Same(sut.Vms[0], sut.SelectedVm);
        Assert.True(sut.HasSelection);
    }

    [Fact]
    public async Task ListItem_UsesNameFallback_AndComposesSummary()
    {
        VmRepository repo = NewRepository();
        await repo.CreateAsync(new VmConfig
        {
            Name = "   ",
            Arch = "x86_64",
            MemoryMiB = 4096,
            Cpu = new CpuConfig { Sockets = 1, Cores = 4, Threads = 1 },
        });
        var sut = NewSut(repo);

        await sut.RefreshCommand.ExecuteAsync(null);

        VmListItemViewModel item = Assert.Single(sut.Vms);
        Assert.Equal("(unnamed VM)", item.Name);
        Assert.Contains("x86_64", item.Summary, StringComparison.Ordinal);
        Assert.Contains("4 vCPU", item.Summary, StringComparison.Ordinal);
        Assert.Contains("4096 MiB", item.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refresh_PreservesRunningItems()
    {
        VmRepository repo = NewRepository();
        await repo.CreateAsync(new VmConfig { Name = "keep-running" });
        var sut = NewSut(repo);
        await sut.RefreshCommand.ExecuteAsync(null);
        VmListItemViewModel item = sut.Vms[0];
        await item.StartCommand.ExecuteAsync(null);
        Assert.Equal(VmStatus.Running, item.Status);

        await sut.RefreshCommand.ExecuteAsync(null);

        Assert.Same(item, sut.Vms[0]);
        Assert.Equal(VmStatus.Running, item.Status);
    }

    [Fact]
    public async Task DeletingTheOnlyVm_EmptiesTheList()
    {
        VmRepository repo = NewRepository();
        await repo.CreateAsync(new VmConfig { Name = "gone" });
        var sut = NewSut(repo);
        await sut.RefreshCommand.ExecuteAsync(null);
        VmListItemViewModel item = sut.Vms[0];

        item.DeleteCommand.Execute(null);
        await item.ConfirmDeleteCommand.ExecuteAsync(null);

        Assert.Empty(sut.Vms);
        Assert.True(sut.IsEmpty);
    }

    [Fact]
    public async Task AddCreated_InsertsSorted_AndSelectsTheNewVm()
    {
        VmRepository repo = NewRepository();
        await repo.CreateAsync(new VmConfig { Name = "Beta" });
        var sut = NewSut(repo);
        await sut.RefreshCommand.ExecuteAsync(null);

        Vm created = new(Path.Combine(_root, "new-id"), new VmConfig { Id = "new-id", Name = "Alpha" });
        sut.AddCreated(created);

        Assert.Equal(2, sut.Vms.Count);
        Assert.Equal("Alpha", sut.Vms[0].Name);
        Assert.Same(sut.Vms[0], sut.SelectedVm);
        Assert.True(sut.HasSelection);
    }
}

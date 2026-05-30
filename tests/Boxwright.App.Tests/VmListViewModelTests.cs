using Boxwright.App.ViewModels;
using Boxwright.Core;
using Xunit;

namespace Boxwright.App.Tests;

/// <summary>
/// Exercises <see cref="VmListViewModel"/> against a real <see cref="VmRepository"/>
/// over a throwaway temp directory — no UI, no mocks.
/// </summary>
public sealed class VmListViewModelTests : IDisposable
{
    private readonly string _root;

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
            // Best-effort cleanup of the temp directory.
        }
    }

    private VmRepository NewRepository() => new(_root);

    [Fact]
    public async Task Refresh_WithNoVms_LeavesListEmptyAndFlagsEmpty()
    {
        var sut = new VmListViewModel(NewRepository());

        await sut.RefreshCommand.ExecuteAsync(null);

        Assert.Empty(sut.Vms);
        Assert.True(sut.IsEmpty);
        Assert.False(sut.IsLoading);
        Assert.False(sut.HasError);
    }

    [Fact]
    public async Task Refresh_LoadsSavedVms_SortedByNameCaseInsensitively()
    {
        VmRepository repo = NewRepository();
        await repo.CreateAsync(new VmConfig { Name = "ubuntu" });
        await repo.CreateAsync(new VmConfig { Name = "Alpine" });
        await repo.CreateAsync(new VmConfig { Name = "debian" });
        var sut = new VmListViewModel(repo);

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
        var sut = new VmListViewModel(repo);

        await sut.RefreshCommand.ExecuteAsync(null);
        await sut.RefreshCommand.ExecuteAsync(null);

        Assert.Single(sut.Vms);
    }

    [Fact]
    public async Task SelectedVm_DefaultsToNull_AndTracksAssignment()
    {
        VmRepository repo = NewRepository();
        await repo.CreateAsync(new VmConfig { Name = "Pick me" });
        var sut = new VmListViewModel(repo);
        await sut.RefreshCommand.ExecuteAsync(null);

        Assert.Null(sut.SelectedVm);

        sut.SelectedVm = sut.Vms[0];

        Assert.Same(sut.Vms[0], sut.SelectedVm);
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
        var sut = new VmListViewModel(repo);

        await sut.RefreshCommand.ExecuteAsync(null);

        VmListItemViewModel item = Assert.Single(sut.Vms);
        Assert.Equal("(unnamed VM)", item.Name);
        Assert.Contains("x86_64", item.Summary, StringComparison.Ordinal);
        Assert.Contains("4 vCPU", item.Summary, StringComparison.Ordinal);
        Assert.Contains("4096 MiB", item.Summary, StringComparison.Ordinal);
    }
}

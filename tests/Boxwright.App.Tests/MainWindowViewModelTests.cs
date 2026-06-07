using Boxwright.App.Services;
using Boxwright.App.ViewModels;
using Boxwright.Core;
using Xunit;

namespace Boxwright.App.Tests;

/// <summary>Exercises <see cref="MainWindowViewModel"/> — currently the logs-folder toolbar affordance.</summary>
public sealed class MainWindowViewModelTests : IDisposable
{
    private readonly string _root;

    public MainWindowViewModelTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "boxwright-mwvm-tests", Guid.NewGuid().ToString("N"));
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

    [Fact]
    public void OpenLogsFolder_RevealsTheAppLogsDirectory()
    {
        var opener = new FakeFolderOpener();
        MainWindowViewModel sut = NewSut(opener);

        sut.OpenLogsFolderCommand.Execute(null);

        Assert.Equal(AppPaths.LogsDirectory, opener.LastPath);
    }

    private MainWindowViewModel NewSut(FakeFolderOpener opener)
    {
        var repository = new VmRepository(_root);
        var vms = new VmListViewModel(
            repository,
            new FakeVmLauncher(new FakeRunningVm()),
            new ImmediateUiDispatcher(),
            new FakeFilePicker(),
            new FakeDisplayLauncher(),
            new FakeEmbeddedVncDisplay(),
            new FakeLogReader(),
            new FakeSnapshotService(),
            new FakeVmCloneService(),
            new FakeLiveSnapshotService());
        return new MainWindowViewModel(
            vms, AcceleratorDetector.CreateDefault(), repository, new FakeDiskService(), opener,
            new FakeOsCatalogSource(), new FakeIsoDownloader(), new FakeSeedGenerator(),
            new FakeUnattendedInstallerResolver(new FakeUnattendedInstaller()), new FakeAutounattendSeedGenerator(),
            new FakeFilePicker(), new ImmediateUiDispatcher());
    }
}

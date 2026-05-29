using Xunit;

namespace Boxwright.Core.Tests;

// CORE-10: DisplayLauncher launches remote-viewer against the SPICE port (ADR-0004).
public class DisplayLauncherTests
{
    [Fact]
    public void Launch_WhenViewerFound_StartsRemoteViewerWithSpiceUri()
    {
        var launcher = new FakeProcessLauncher();
        var display = new DisplayLauncher(launcher, new FakeRemoteViewerLocator("/usr/bin/remote-viewer"));

        display.Launch(5930);

        Assert.NotNull(launcher.LastRequest);
        Assert.Equal("/usr/bin/remote-viewer", launcher.LastRequest!.Executable);
        Assert.Equal("spice://127.0.0.1:5930", Assert.Single(launcher.LastRequest.Arguments));
    }

    [Fact]
    public void Launch_WhenViewerNotFound_ThrowsDisplayException()
    {
        var display = new DisplayLauncher(new FakeProcessLauncher(), new FakeRemoteViewerLocator(null));

        Assert.Throws<DisplayException>(() => display.Launch(5930));
    }

    [Fact]
    public void Launch_RejectsNonPositivePort()
    {
        var display = new DisplayLauncher(new FakeProcessLauncher(), new FakeRemoteViewerLocator("remote-viewer"));

        Assert.Throws<ArgumentOutOfRangeException>(() => display.Launch(0));
    }

    private sealed class FakeRemoteViewerLocator(string? path) : IRemoteViewerLocator
    {
        public string? Locate() => path;
    }
}

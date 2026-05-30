using Microsoft.Extensions.Logging;
using Xunit;

namespace Boxwright.App.Tests;

// The BOXWRIGHT_LOG_LEVEL override is the only way to surface QMP traffic + full command
// lines in a packaged build, so its parse is worth pinning down.
public class ServiceConfigurationTests
{
    [Theory]
    [InlineData("Debug", LogLevel.Debug)]
    [InlineData("debug", LogLevel.Debug)]
    [InlineData("TRACE", LogLevel.Trace)]
    [InlineData("Warning", LogLevel.Warning)]
    public void ResolveMinimumLevel_ParsesKnownLevelNames(string configured, LogLevel expected) =>
        Assert.Equal(expected, ServiceConfiguration.ResolveMinimumLevel(configured));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("999")] // a numeric value that isn't a defined LogLevel
    public void ResolveMinimumLevel_DefaultsToInformation_WhenUnsetOrUnrecognized(string? configured) =>
        Assert.Equal(LogLevel.Information, ServiceConfiguration.ResolveMinimumLevel(configured));
}

using Xunit;

namespace Boxwright.Core.Tests;

public sealed class NetworkValidationTests
{
    [Theory]
    [InlineData("bridge", true)]
    [InlineData("tap", true)]
    [InlineData("Bridge", true)]
    [InlineData("user", false)]
    [InlineData("", false)]
    public void RequiresLinux_IsTrueOnlyForBridgeAndTap(string mode, bool expected) =>
        Assert.Equal(expected, NetworkValidation.RequiresLinux(mode));

    [Theory]
    [InlineData("bridge")]
    [InlineData("tap")]
    public void EnsureSupportedOnHost_ThrowsForLinuxOnlyModes_OnNonLinux(string mode)
    {
        var net = new NetworkConfig { Mode = mode };

        VmConfigException ex = Assert.Throws<VmConfigException>(() => NetworkValidation.EnsureSupportedOnHost(net, isLinux: false));
        Assert.Contains(mode, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("bridge")]
    [InlineData("tap")]
    public void EnsureSupportedOnHost_AllowsLinuxOnlyModes_OnLinux(string mode)
    {
        NetworkValidation.EnsureSupportedOnHost(new NetworkConfig { Mode = mode }, isLinux: true); // no throw
    }

    [Fact]
    public void EnsureSupportedOnHost_AllowsUserMode_OnAnyHost()
    {
        NetworkValidation.EnsureSupportedOnHost(new NetworkConfig { Mode = "user" }, isLinux: false); // no throw
    }
}

using Boxwright.Core;
using Xunit;

namespace Boxwright.Core.Tests;

public sealed class VirtioWinTests
{
    [Fact]
    public void CatalogEntry_CarriesThePinnedUrlSizeAndHash()
    {
        OsCatalogEntry e = VirtioWin.CatalogEntry;

        Assert.StartsWith("virtio-win-", e.Id);
        Assert.Equal(VirtioWin.IsoUrl, e.IsoUrl);
        Assert.Equal(VirtioWin.SizeBytes, e.SizeBytes);
        Assert.True(e.SizeBytes > 0);
        Assert.Matches("^[0-9a-f]{64}$", e.Sha256); // a real, lowercase-hex SHA-256 (not the placeholder)
        Assert.Equal(VirtioWin.Sha256, e.Sha256);
    }

    [Fact]
    public void DriverFolders_AreTheVirtioWinLayout()
    {
        Assert.Equal("viostor", VirtioWin.StorageDriver); // virtio-blk
        Assert.Equal("NetKVM", VirtioWin.NetworkDriver);  // virtio-net
        Assert.Equal("w11", VirtioWin.WindowsFolder);
        Assert.Equal("amd64", VirtioWin.Arch);
    }
}

using Xunit;

namespace Boxwright.Core.Tests;

// LinuxUsbDeviceEnumerator parses a sysfs-shaped tree (no real /sys needed): each device dir has
// idVendor/idProduct (+ optional manufacturer/product); interface nodes and root hubs are skipped.
public sealed class LinuxUsbDeviceEnumeratorTests : IDisposable
{
    private readonly string _root;

    public LinuxUsbDeviceEnumeratorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"boxwright-sysfs-{Guid.NewGuid():N}");
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

    private void WriteDevice(string name, string? vendor, string? product, string? manufacturer = null, string? productName = null)
    {
        string dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        if (vendor is not null)
        {
            File.WriteAllText(Path.Combine(dir, "idVendor"), vendor + "\n");
        }

        if (product is not null)
        {
            File.WriteAllText(Path.Combine(dir, "idProduct"), product + "\n");
        }

        if (manufacturer is not null)
        {
            File.WriteAllText(Path.Combine(dir, "manufacturer"), manufacturer + "\n");
        }

        if (productName is not null)
        {
            File.WriteAllText(Path.Combine(dir, "product"), productName + "\n");
        }
    }

    [Fact]
    public async Task ListAsync_ReturnsDevices_SkipsInterfaceNodesAndRootHubs()
    {
        WriteDevice("1-1", "046d", "c52b", "Logitech", "USB Receiver");
        WriteDevice("2-1", "0408", "5374"); // no manufacturer/product text
        WriteDevice("usb1", "1d6b", "0002", "Linux Foundation", "2.0 root hub"); // root hub → skipped
        WriteDevice("1-1.0", null, null);  // interface node (no ids) → skipped (':' is illegal on Windows paths)

        var enumerator = new LinuxUsbDeviceEnumerator(_root);
        IReadOnlyList<HostUsbDevice> devices = await enumerator.ListAsync();

        Assert.Equal(2, devices.Count);
        Assert.Contains(devices, d => d is { VendorId: "046d", ProductId: "c52b", Description: "Logitech USB Receiver" });
        Assert.Contains(devices, d => d is { VendorId: "0408", ProductId: "5374", Description: "" });
        Assert.DoesNotContain(devices, d => d.VendorId == "1d6b");
    }

    [Fact]
    public async Task ListAsync_NormalizesHexToLowercase()
    {
        WriteDevice("1-2", "046D", "C52B"); // sysfs is lowercase, but be defensive

        var enumerator = new LinuxUsbDeviceEnumerator(_root);
        HostUsbDevice device = Assert.Single(await enumerator.ListAsync());

        Assert.Equal("046d", device.VendorId);
        Assert.Equal("c52b", device.ProductId);
    }

    [Fact]
    public async Task ListAsync_MissingRoot_ReturnsEmpty()
    {
        var enumerator = new LinuxUsbDeviceEnumerator(Path.Combine(_root, "does-not-exist"));

        Assert.Empty(await enumerator.ListAsync());
    }

    [Fact]
    public void IsSupported_RequiresLinuxAndAnExistingRoot()
    {
        Assert.False(new LinuxUsbDeviceEnumerator(Path.Combine(_root, "missing")).IsSupported);
        // On a non-Linux host IsSupported is false regardless; on Linux it tracks the root's existence.
        Assert.Equal(OperatingSystem.IsLinux(), new LinuxUsbDeviceEnumerator(_root).IsSupported);
    }

    [Fact]
    public async Task Unsupported_Throws_AndReportsUnsupported()
    {
        var enumerator = new UnsupportedUsbDeviceEnumerator();

        Assert.False(enumerator.IsSupported);
        await Assert.ThrowsAsync<NotSupportedException>(() => enumerator.ListAsync());
    }
}

using Xunit;

namespace Boxwright.Core.Tests;

// The platform-specific enumerators can't run on this host, but their parsing (the bug-prone part) is
// pure and tested here: macOS system_profiler JSON and Windows hardware-id strings.
public sealed class UsbEnumeratorParsingTests
{
    [Fact]
    public void MacOs_ParsesNestedDevices_SkipsNodesWithoutIds()
    {
        const string json = """
        {
          "SPUSBDataType": [
            {
              "_name": "USB31Bus",
              "_items": [
                {
                  "_name": "USB Receiver",
                  "manufacturer": "Logitech",
                  "vendor_id": "0x046d  (Logitech Inc.)",
                  "product_id": "0xc52b",
                  "_items": [
                    { "_name": "Keyboard", "vendor_id": "0x05ac", "product_id": "0x024f" }
                  ]
                },
                { "_name": "A hub with no ids" }
              ]
            }
          ]
        }
        """;

        IReadOnlyList<HostUsbDevice> devices = MacOsUsbDeviceEnumerator.ParseSystemProfilerJson(json);

        Assert.Equal(2, devices.Count);
        Assert.Contains(devices, d => d is { VendorId: "046d", ProductId: "c52b", Description: "Logitech USB Receiver" });
        Assert.Contains(devices, d => d is { VendorId: "05ac", ProductId: "024f" });
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"SPUSBDataType\":[]}")]
    public void MacOs_EmptyOrMalformed_ReturnsEmpty(string json)
    {
        Assert.Empty(MacOsUsbDeviceEnumerator.ParseSystemProfilerJson(json));
    }

    [Theory]
    [InlineData(@"USB\VID_046D&PID_C52B&REV_2400", "046d", "c52b")]
    [InlineData(@"usb\vid_046d&pid_c52b", "046d", "c52b")]
    [InlineData(@"USB\VID_0408&PID_5374", "0408", "5374")]
    public void Windows_ParsesVidPidFromHardwareId(string hardwareId, string vendor, string product)
    {
        Assert.True(UsbHardwareId.TryParse(hardwareId, out string v, out string p));
        Assert.Equal(vendor, v);
        Assert.Equal(product, p);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"USB\COMPOSITE")]      // no VID/PID
    [InlineData(@"USB\VID_04&PID_C52B")] // vendor too short to be 4 hex
    public void Windows_RejectsNonUsbOrMalformedIds(string? hardwareId)
    {
        Assert.False(UsbHardwareId.TryParse(hardwareId, out _, out _));
    }

    [Fact]
    public void CreateDefault_ReturnsAnEnumeratorForThisHost()
    {
        IUsbDeviceEnumerator enumerator = UsbDeviceEnumerator.CreateDefault(new FakeProcessRunner(0));

        Assert.NotNull(enumerator);
        if (OperatingSystem.IsLinux())
        {
            Assert.IsType<LinuxUsbDeviceEnumerator>(enumerator);
        }
    }
}

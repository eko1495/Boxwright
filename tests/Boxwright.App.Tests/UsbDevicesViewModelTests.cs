using Boxwright.App.ViewModels;
using Boxwright.Core;
using Xunit;

namespace Boxwright.App.Tests;

public sealed class UsbDevicesViewModelTests
{
    private static UsbDevicesViewModel Build(
        FakeUsbDeviceEnumerator? enumerator = null,
        params UsbPassthroughConfig[] initial) =>
        new(enumerator ?? new FakeUsbDeviceEnumerator(), initial);

    [Fact]
    public void Ctor_SeedsConfiguredDevices()
    {
        UsbDevicesViewModel vm = Build(null, new UsbPassthroughConfig { VendorId = "046d", ProductId = "c52b" });

        Assert.Single(vm.Devices);
        Assert.True(vm.CanListHost);
    }

    [Fact]
    public void CanListHost_ReflectsEnumeratorSupport()
    {
        UsbDevicesViewModel vm = Build(new FakeUsbDeviceEnumerator { IsSupported = false });

        Assert.False(vm.CanListHost);
        Assert.True(vm.HostListingUnsupported);
    }

    [Fact]
    public async Task LoadHostDevices_PopulatesFromEnumerator()
    {
        var enumerator = new FakeUsbDeviceEnumerator();
        enumerator.Devices.Add(new HostUsbDevice("046d", "c52b", "Logitech"));
        UsbDevicesViewModel vm = Build(enumerator);

        await vm.LoadHostDevicesCommand.ExecuteAsync(null);

        HostUsbDevice host = Assert.Single(vm.HostDevices);
        Assert.Equal("046d", host.VendorId);
    }

    [Fact]
    public async Task LoadHostDevices_WhenUnsupported_IsANoOp()
    {
        UsbDevicesViewModel vm = Build(new FakeUsbDeviceEnumerator { IsSupported = false });

        await vm.LoadHostDevicesCommand.ExecuteAsync(null);

        Assert.Empty(vm.HostDevices);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public void AddSelected_AddsTheChosenHostDevice()
    {
        UsbDevicesViewModel vm = Build();
        vm.SelectedHostDevice = new HostUsbDevice("046d", "c52b", "Logitech");

        vm.AddSelectedCommand.Execute(null);

        UsbPassthroughConfig added = Assert.Single(vm.Devices);
        Assert.Equal("046d", added.VendorId);
        Assert.Equal("c52b", added.ProductId);
        Assert.Equal("Logitech", added.Description);
    }

    [Fact]
    public void AddManual_ValidId_AddsAndClearsEntry()
    {
        UsbDevicesViewModel vm = Build();
        vm.ManualEntry = "046D:C52B";

        vm.AddManualCommand.Execute(null);

        UsbPassthroughConfig added = Assert.Single(vm.Devices);
        Assert.Equal("046d", added.VendorId);
        Assert.Equal("c52b", added.ProductId);
        Assert.Equal(string.Empty, vm.ManualEntry);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("046d")]
    [InlineData("gggg:c52b")]
    public void AddManual_InvalidId_SetsErrorAndAddsNothing(string entry)
    {
        UsbDevicesViewModel vm = Build();
        vm.ManualEntry = entry;

        vm.AddManualCommand.Execute(null);

        Assert.Empty(vm.Devices);
        Assert.True(vm.HasErrorMessage);
    }

    [Fact]
    public void AddManual_Duplicate_IsRejected()
    {
        UsbDevicesViewModel vm = Build(null, new UsbPassthroughConfig { VendorId = "046d", ProductId = "c52b" });
        vm.ManualEntry = "046d:c52b";

        vm.AddManualCommand.Execute(null);

        Assert.Single(vm.Devices);
        Assert.True(vm.HasErrorMessage);
    }

    [Fact]
    public void Remove_DropsTheDevice()
    {
        var device = new UsbPassthroughConfig { VendorId = "046d", ProductId = "c52b" };
        UsbDevicesViewModel vm = Build(null, device);

        vm.RemoveCommand.Execute(device);

        Assert.Empty(vm.Devices);
    }
}

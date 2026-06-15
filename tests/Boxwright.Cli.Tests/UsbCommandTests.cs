using System.Text.Json;
using Boxwright.Cli.Commands;
using Boxwright.Core;
using Boxwright.Qmp;
using Xunit;

namespace Boxwright.Cli.Tests;

public sealed class UsbCommandTests
{
    private static UsbCommand Build(
        TempVmStore store,
        CapturingOutput output,
        IUsbDeviceEnumerator? enumerator = null,
        IVmStatusProbe? probe = null,
        IVmLauncher? launcher = null) =>
        new(enumerator ?? new FakeUsbDeviceEnumerator(),
            new VmResolver(store.Repository),
            store.Repository,
            probe ?? new FakeStatusProbe(),
            launcher ?? new FakeVmLauncher(),
            output.Cli);

    [Fact]
    public async Task List_RendersHostDevices()
    {
        using var store = new TempVmStore();
        var enumerator = new FakeUsbDeviceEnumerator();
        enumerator.Devices.Add(new HostUsbDevice("046d", "c52b", "Logitech USB Receiver"));
        var output = new CapturingOutput();
        UsbCommand command = Build(store, output, enumerator);

        int code = await command.RunAsync(ParsedArgs.Parse(["list"]), CancellationToken.None);

        Assert.Equal(0, code);
        Assert.Contains("046d:c52b", output.Out, StringComparison.Ordinal);
        Assert.Contains("Logitech USB Receiver", output.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_WhenUnsupported_ExplainsAndStillReturnsZero()
    {
        using var store = new TempVmStore();
        var output = new CapturingOutput();
        UsbCommand command = Build(store, output, new FakeUsbDeviceEnumerator { IsSupported = false });

        int code = await command.RunAsync(ParsedArgs.Parse(["list"]), CancellationToken.None);

        Assert.Equal(0, code);
        Assert.Contains("isn't supported", output.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_Json_WhenUnsupported_IsAnEmptyArray()
    {
        using var store = new TempVmStore();
        var output = new CapturingOutput();
        UsbCommand command = Build(store, output, new FakeUsbDeviceEnumerator { IsSupported = false });

        await command.RunAsync(ParsedArgs.Parse(["list", "--json"]), CancellationToken.None);

        using JsonDocument doc = JsonDocument.Parse(output.Out);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(0, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task Add_PersistsToConfig()
    {
        using var store = new TempVmStore();
        Vm vm = store.Add("box");
        UsbCommand command = Build(store, new CapturingOutput());

        await command.RunAsync(ParsedArgs.Parse(["add", "box", "046d:c52b", "--description=receiver"]), CancellationToken.None);

        Vm reloaded = Assert.Single(await store.Repository.ListAsync());
        UsbPassthroughConfig device = Assert.Single(reloaded.Config.UsbDevices);
        Assert.Equal("046d", device.VendorId);
        Assert.Equal("c52b", device.ProductId);
        Assert.Equal("receiver", device.Description);
    }

    [Fact]
    public async Task Add_NormalizesUppercaseHex()
    {
        using var store = new TempVmStore();
        store.Add("box");
        UsbCommand command = Build(store, new CapturingOutput());

        await command.RunAsync(ParsedArgs.Parse(["add", "box", "046D:C52B"]), CancellationToken.None);

        UsbPassthroughConfig device = Assert.Single((await store.Repository.ListAsync())[0].Config.UsbDevices);
        Assert.Equal("046d", device.VendorId);
        Assert.Equal("c52b", device.ProductId);
    }

    [Fact]
    public async Task Add_Duplicate_IsRejected()
    {
        using var store = new TempVmStore();
        store.Add("box");
        UsbCommand command = Build(store, new CapturingOutput());
        await command.RunAsync(ParsedArgs.Parse(["add", "box", "046d:c52b"]), CancellationToken.None);

        await Assert.ThrowsAsync<CliException>(() =>
            command.RunAsync(ParsedArgs.Parse(["add", "box", "046d:c52b"]), CancellationToken.None));
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("046d")]
    [InlineData("046d:c52")]
    [InlineData("gggg:c52b")]
    public async Task Add_InvalidId_IsRejected(string id)
    {
        using var store = new TempVmStore();
        store.Add("box");
        UsbCommand command = Build(store, new CapturingOutput());

        await Assert.ThrowsAsync<CliException>(() =>
            command.RunAsync(ParsedArgs.Parse(["add", "box", id]), CancellationToken.None));
    }

    [Fact]
    public async Task Remove_DropsTheDevice()
    {
        using var store = new TempVmStore();
        store.Add("box");
        UsbCommand command = Build(store, new CapturingOutput());
        await command.RunAsync(ParsedArgs.Parse(["add", "box", "046d:c52b"]), CancellationToken.None);

        await command.RunAsync(ParsedArgs.Parse(["remove", "box", "046d:c52b"]), CancellationToken.None);

        Assert.Empty((await store.Repository.ListAsync())[0].Config.UsbDevices);
    }

    [Fact]
    public async Task Remove_NotPresent_IsAnError()
    {
        using var store = new TempVmStore();
        store.Add("box");
        UsbCommand command = Build(store, new CapturingOutput());

        await Assert.ThrowsAsync<CliException>(() =>
            command.RunAsync(ParsedArgs.Parse(["remove", "box", "046d:c52b"]), CancellationToken.None));
    }

    [Fact]
    public async Task Add_ToRunningVm_NotesNextBoot()
    {
        using var store = new TempVmStore();
        Vm vm = store.Add("box");
        var probe = new FakeStatusProbe();
        probe.MarkRunning(vm.Config.Id);
        var output = new CapturingOutput();
        UsbCommand command = Build(store, output, probe: probe);

        await command.RunAsync(ParsedArgs.Parse(["add", "box", "046d:c52b"]), CancellationToken.None);

        Assert.Contains("next boot", output.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Show_Json_ListsConfiguredDevices()
    {
        using var store = new TempVmStore();
        store.Add("box");
        UsbCommand command = Build(store, new CapturingOutput());
        await command.RunAsync(ParsedArgs.Parse(["add", "box", "046d:c52b", "--description=pad"]), CancellationToken.None);

        var output = new CapturingOutput();
        UsbCommand show = Build(store, output);
        await show.RunAsync(ParsedArgs.Parse(["show", "box", "--json"]), CancellationToken.None);

        using JsonDocument doc = JsonDocument.Parse(output.Out);
        JsonElement entry = Assert.Single(doc.RootElement.EnumerateArray().ToArray());
        Assert.Equal("046d:c52b", entry.GetProperty("id").GetString());
        Assert.Equal("pad", entry.GetProperty("description").GetString());
    }

    [Fact]
    public async Task Add_Now_HotPlugsTheRunningVm()
    {
        using var store = new TempVmStore();
        store.Add("box");
        var running = new FakeRunningVm();
        var output = new CapturingOutput();
        UsbCommand command = Build(store, output, launcher: new FakeVmLauncher { AdoptResult = running });

        await command.RunAsync(ParsedArgs.Parse(["add", "box", "046d:c52b", "--now"]), CancellationToken.None);

        Assert.Equal(("046d", "c52b"), Assert.Single(running.Attached));
        Assert.Contains("Attached to the running VM now", output.Out, StringComparison.Ordinal);
        // Still persisted (next-boot too).
        Assert.Single((await store.Repository.ListAsync())[0].Config.UsbDevices);
    }

    [Fact]
    public async Task Remove_Now_HotUnplugsTheRunningVm()
    {
        using var store = new TempVmStore();
        store.Add("box");
        var running = new FakeRunningVm();
        UsbCommand add = Build(store, new CapturingOutput());
        await add.RunAsync(ParsedArgs.Parse(["add", "box", "046d:c52b"]), CancellationToken.None);

        var output = new CapturingOutput();
        UsbCommand remove = Build(store, output, launcher: new FakeVmLauncher { AdoptResult = running });
        await remove.RunAsync(ParsedArgs.Parse(["remove", "box", "046d:c52b", "--now"]), CancellationToken.None);

        Assert.Equal(("046d", "c52b"), Assert.Single(running.Detached));
        Assert.Empty((await store.Repository.ListAsync())[0].Config.UsbDevices);
    }

    [Fact]
    public async Task Add_Now_WhenNotRunning_SaysNoEffect()
    {
        using var store = new TempVmStore();
        store.Add("box");
        var output = new CapturingOutput();
        // FakeVmLauncher with no AdoptResult → AdoptAsync returns null (not running).
        UsbCommand command = Build(store, output, launcher: new FakeVmLauncher());

        await command.RunAsync(ParsedArgs.Parse(["add", "box", "046d:c52b", "--now"]), CancellationToken.None);

        Assert.Contains("had no effect", output.Out, StringComparison.Ordinal);
        Assert.Single((await store.Repository.ListAsync())[0].Config.UsbDevices); // still persisted
    }

    [Fact]
    public async Task Add_Now_WhenQemuRejects_IsACleanError()
    {
        using var store = new TempVmStore();
        store.Add("box");
        var running = new FakeRunningVm { UsbFailure = new QmpCommandException("GenericError", "no such device") };
        UsbCommand command = Build(store, new CapturingOutput(), launcher: new FakeVmLauncher { AdoptResult = running });

        CliException ex = await Assert.ThrowsAsync<CliException>(() =>
            command.RunAsync(ParsedArgs.Parse(["add", "box", "046d:c52b", "--now"]), CancellationToken.None));

        Assert.Contains("no such device", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownSubcommand_IsAnError()
    {
        using var store = new TempVmStore();
        UsbCommand command = Build(store, new CapturingOutput());

        await Assert.ThrowsAsync<CliException>(() =>
            command.RunAsync(ParsedArgs.Parse(["frobnicate"]), CancellationToken.None));
    }
}

using System.Text.Json;
using Boxwright.Qmp;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Boxwright.Core.Tests;

// CORE-9: VM lifecycle (start + power actions), via a fake launcher and a
// recording QMP client (no real QEMU, no real socket).
public class VmLauncherTests
{
    [Fact]
    public async Task StartAsync_SpawnsQemuAndReturnsRunningVm()
    {
        await WithStartedVmAsync((running, launcher, _) =>
        {
            Assert.Equal(QemuProcessState.Running, running.State);
            Assert.Equal(Accelerator.Tcg, running.Accelerator);
            Assert.InRange(running.SpicePort, 1, 65535);

            Assert.NotNull(launcher.LastRequest);
            Assert.EndsWith(ExeName("qemu-system-x86_64"), launcher.LastRequest!.Executable, StringComparison.Ordinal);
            Assert.Contains("-accel", launcher.LastRequest.Arguments);
            Assert.Contains("tcg", launcher.LastRequest.Arguments);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task RequestShutdownAsync_IssuesSystemPowerdown()
    {
        await WithStartedVmAsync(async (running, _, recording) =>
        {
            await running.RequestShutdownAsync();
            Assert.Contains("system_powerdown", recording.Commands);
        });
    }

    [Fact]
    public async Task PauseResumeReset_IssueExpectedQmpCommands()
    {
        await WithStartedVmAsync(async (running, _, recording) =>
        {
            await running.PauseAsync();
            await running.ResumeAsync();
            await running.ResetAsync();
            Assert.Equal("stop cont system_reset", string.Join(' ', recording.Commands));
        });
    }

    [Fact]
    public async Task EjectIsoAsync_IssuesQmpEject()
    {
        await WithStartedVmAsync(async (running, _, recording) =>
        {
            await running.EjectIsoAsync();
            Assert.Contains("eject", recording.Commands);
        });
    }

    [Fact]
    public async Task SaveLoadDeleteState_BridgeToTheHumanMonitor()
    {
        await WithStartedVmAsync(async (running, _, recording) =>
        {
            await running.SaveStateAsync("boxwright-saved-state");
            await running.LoadStateAsync("boxwright-saved-state");
            await running.DeleteStateAsync("boxwright-saved-state");

            Assert.Equal(
                "savevm boxwright-saved-state loadvm boxwright-saved-state delvm boxwright-saved-state",
                string.Join(' ', recording.MonitorCommands));
        });
    }

    [Fact]
    public async Task AttachUsbAsync_IssuesDeviceAddWithUsbHostVendorProduct()
    {
        await WithStartedVmAsync(async (running, _, recording) =>
        {
            await running.AttachUsbAsync("046d", "c52b");

            int i = recording.Commands.IndexOf("device_add");
            Assert.True(i >= 0, "device_add was not issued");
            string payload = recording.CommandPayloads[i];
            Assert.Contains("\"driver\":\"usb-host\"", payload, StringComparison.Ordinal);
            Assert.Contains("\"id\":\"usb-046d-c52b\"", payload, StringComparison.Ordinal);
            Assert.Contains("\"vendorid\":1133", payload, StringComparison.Ordinal);   // 0x046d
            Assert.Contains("\"productid\":50475", payload, StringComparison.Ordinal); // 0xc52b
        });
    }

    [Fact]
    public async Task DetachUsbAsync_IssuesDeviceDelByStableId()
    {
        await WithStartedVmAsync(async (running, _, recording) =>
        {
            await running.DetachUsbAsync("046d", "c52b");

            int i = recording.Commands.IndexOf("device_del");
            Assert.True(i >= 0, "device_del was not issued");
            Assert.Contains("\"id\":\"usb-046d-c52b\"", recording.CommandPayloads[i], StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task TakeLiveSnapshotAsync_NoMatchingQcow2Device_Throws()
    {
        await WithStartedVmAsync(async (running, _, _) =>
        {
            // query-block reports no devices (the recording client returns {}), so nothing backs the
            // requested active image — the live snapshot must fail loudly rather than silently no-op.
            var disks = new[] { new LiveSnapshotDiskRequest("/vm/disk.qcow2", "/vm/disk.snap.qcow2") };

            await Assert.ThrowsAsync<InvalidOperationException>(() => running.TakeLiveSnapshotAsync(disks));
        });
    }

    [Fact]
    public async Task TakeLiveSnapshotAsync_NoDisks_Throws()
    {
        await WithStartedVmAsync(async (running, _, _) =>
        {
            await Assert.ThrowsAsync<ArgumentException>(() => running.TakeLiveSnapshotAsync([]));
        });
    }

    [Fact]
    public async Task ForceStop_TerminatesProcess()
    {
        await WithStartedVmAsync((running, _, _) =>
        {
            running.ForceStop();
            Assert.Equal(QemuProcessState.Exited, running.State);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task StopAsync_AfterGracePeriod_ForcesKill()
    {
        await WithStartedVmAsync(async (running, _, recording) =>
        {
            // The fake process never exits on its own, so the grace period elapses and it is killed.
            await running.StopAsync(TimeSpan.FromMilliseconds(20));

            Assert.Contains("system_powerdown", recording.Commands);
            Assert.Equal(QemuProcessState.Exited, running.State);
        });
    }

    [Fact]
    public async Task StartAsync_UefiVm_UsesPflashFirmware_AndCreatesPerVmVars()
    {
        await WithStartedVmAsync(
            (running, launcher, _) =>
            {
                Assert.Equal(QemuProcessState.Running, running.State);
                IReadOnlyList<string> args = launcher.LastRequest!.Arguments;
                Assert.Contains(args, a => a.StartsWith("if=pflash,format=raw,unit=0,readonly=on", StringComparison.Ordinal));

                string varsArg = args.First(a => a.Contains("unit=1", StringComparison.Ordinal));
                string varsPath = varsArg["if=pflash,format=raw,unit=1,file=".Length..];
                Assert.True(File.Exists(varsPath)); // the per-VM NVRAM copy was created
                return Task.CompletedTask;
            },
            firmware: "uefi");
    }

    [Fact]
    public async Task AdoptAsync_WithLiveProcessAndRuntimeState_ReconnectsAndReturnsRunningVm()
    {
        await WithLauncherAsync(async (vmLauncher, vm, launcher, store) =>
        {
            // A VM launched earlier (runtime.json present) whose QEMU is still alive.
            store.Save(vm, VmRuntimeState.From(4321, QmpEndpoint.Tcp("127.0.0.1", 4444), 5930, "spice", 5931, Accelerator.Whpx));
            launcher.AttachResult = new FakeRunningProcess(); // alive

            IRunningVm? adopted = await vmLauncher.AdoptAsync(vm);

            Assert.NotNull(adopted);
            Assert.Equal(4321, launcher.LastAttachedId);
            Assert.Equal(5930, adopted!.SpicePort);
            Assert.Equal("spice", adopted.DisplayProtocol);
            Assert.Equal(Accelerator.Whpx, adopted.Accelerator);
            Assert.Equal(QemuProcessState.Running, adopted.State);
            await adopted.DisposeAsync();
        });
    }

    [Fact]
    public async Task AdoptAsync_NoRuntimeState_ReturnsNull()
    {
        await WithLauncherAsync(async (vmLauncher, vm, launcher, _) =>
        {
            Assert.Null(await vmLauncher.AdoptAsync(vm));
            Assert.Null(launcher.LastAttachedId); // never even tried to attach
        });
    }

    [Fact]
    public async Task AdoptAsync_ProcessGone_ReturnsNullAndClearsStaleState()
    {
        await WithLauncherAsync(async (vmLauncher, vm, launcher, store) =>
        {
            store.Save(vm, VmRuntimeState.From(9999, QmpEndpoint.Tcp("127.0.0.1", 4444), 5930, "spice", 5931, Accelerator.Tcg));
            launcher.AttachResult = null; // the process is gone / its id was reused

            IRunningVm? adopted = await vmLauncher.AdoptAsync(vm);

            Assert.Null(adopted);
            Assert.Equal(9999, launcher.LastAttachedId);
            Assert.Null(store.TryLoad(vm)); // the stale record was cleared
        });
    }

    private static async Task WithLauncherAsync(Func<VmLauncher, Vm, FakeProcessLauncher, VmRuntimeStore, Task> body)
    {
        string root = Path.Combine(Path.GetTempPath(), $"boxwright-adopt-{Guid.NewGuid():N}");
        string vmFolder = Path.Combine(root, "vm");
        Directory.CreateDirectory(vmFolder);

        var launcher = new FakeProcessLauncher();
        var store = new VmRuntimeStore();
        var vmLauncher = new VmLauncher(
            launcher,
            new QmpEndpointAllocator(),
            new FakeQmpConnector(new RecordingQmpClient()),
            new FakeQgaConnector(),
            store,
            new AcceleratorDetector(new TcgProbe()),
            new QemuLocator(Path.Combine(root, "qemu")),
            NullLogger<VmLauncher>.Instance);
        var vm = new Vm(vmFolder, new VmConfig { Name = "Test" });

        try
        {
            await body(vmLauncher, vm, launcher, store);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string ExeName(string baseName) => OperatingSystem.IsWindows() ? baseName + ".exe" : baseName;

    private static async Task WithStartedVmAsync(Func<IRunningVm, FakeProcessLauncher, RecordingQmpClient, Task> body, string firmware = "bios")
    {
        string root = Path.Combine(Path.GetTempPath(), $"boxwright-launch-{Guid.NewGuid():N}");
        string vmFolder = Path.Combine(root, "vm");
        string qemuDir = Path.Combine(root, "qemu");
        string shareDir = Path.Combine(qemuDir, "share");
        Directory.CreateDirectory(vmFolder);
        Directory.CreateDirectory(shareDir);
        await File.WriteAllTextAsync(Path.Combine(qemuDir, ExeName("qemu-system-x86_64")), "stub");
        await File.WriteAllTextAsync(Path.Combine(shareDir, "edk2-x86_64-code.fd"), "code"); // stub OVMF for UEFI tests
        await File.WriteAllTextAsync(Path.Combine(shareDir, "edk2-i386-vars.fd"), "vars");

        var launcher = new FakeProcessLauncher();
        var recording = new RecordingQmpClient();
        var vmLauncher = new VmLauncher(
            launcher,
            new QmpEndpointAllocator(),
            new FakeQmpConnector(recording),
            new FakeQgaConnector(),
            new VmRuntimeStore(),
            new AcceleratorDetector(new TcgProbe()),
            new QemuLocator(qemuDir),
            NullLogger<VmLauncher>.Instance);
        var vm = new Vm(vmFolder, new VmConfig { Name = "Test", Firmware = firmware });

        try
        {
            IRunningVm running = await vmLauncher.StartAsync(vm);
            await body(running, launcher, recording);
            await running.DisposeAsync();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class TcgProbe : IHostAccelerationProbe
    {
        public bool IsKvmAvailable() => false;

        public bool IsHvfAvailable() => false;

        public bool IsWhpxAvailable() => false;
    }

    private sealed class FakeQmpConnector(IQmpClient client) : IQmpConnector
    {
        public Task<IQmpClient> ConnectAsync(QmpEndpoint endpoint, Func<bool> isProcessAlive, CancellationToken cancellationToken = default) =>
            Task.FromResult(client);
    }

    // No guest agent in unit tests -> StopAsync falls back to ACPI system_powerdown.
    private sealed class FakeQgaConnector : IQgaConnector
    {
        public Task<IQgaClient?> TryConnectAsync(int port, CancellationToken cancellationToken = default) =>
            Task.FromResult<IQgaClient?>(null);
    }

    private sealed class RecordingQmpClient : IQmpClient
    {
        public List<string> Commands { get; } = [];

        public List<string> MonitorCommands { get; } = [];

        /// <summary>The serialized arguments of each <see cref="ExecuteAsync(string, object?, CancellationToken)"/> call (parallel to <see cref="Commands"/>).</summary>
        public List<string> CommandPayloads { get; } = [];

        public bool IsConnected => true;

        public IObservable<QmpEvent> Events => throw new NotSupportedException();

        public Task ConnectAsync(QmpEndpoint endpoint, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<JsonElement> ExecuteAsync(string command, object? arguments = null, CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            CommandPayloads.Add(arguments is null ? string.Empty : JsonSerializer.Serialize(arguments));
            using var empty = JsonDocument.Parse("{}");
            return Task.FromResult(empty.RootElement.Clone());
        }

        public Task<TResult> ExecuteAsync<TResult>(string command, object? arguments = null, CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            if (arguments is IDictionary<string, object> args && args.TryGetValue("command-line", out object? line))
            {
                MonitorCommands.Add(line?.ToString() ?? string.Empty);
            }

            return Task.FromResult(default(TResult)!); // empty/null monitor output = success
        }

        public Task<QmpSchema> GetSchemaAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

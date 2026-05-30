using System.Text.Json;
using Boxwright.Qmp;
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

    private static string ExeName(string baseName) => OperatingSystem.IsWindows() ? baseName + ".exe" : baseName;

    private static async Task WithStartedVmAsync(Func<IRunningVm, FakeProcessLauncher, RecordingQmpClient, Task> body)
    {
        string root = Path.Combine(Path.GetTempPath(), $"boxwright-launch-{Guid.NewGuid():N}");
        string vmFolder = Path.Combine(root, "vm");
        string qemuDir = Path.Combine(root, "qemu");
        Directory.CreateDirectory(vmFolder);
        Directory.CreateDirectory(qemuDir);
        await File.WriteAllTextAsync(Path.Combine(qemuDir, ExeName("qemu-system-x86_64")), "stub");

        var launcher = new FakeProcessLauncher();
        var recording = new RecordingQmpClient();
        var vmLauncher = new VmLauncher(
            launcher,
            new QmpEndpointAllocator(),
            new FakeQmpConnector(recording),
            new AcceleratorDetector(new TcgProbe()),
            new QemuLocator(qemuDir));
        var vm = new Vm(vmFolder, new VmConfig { Name = "Test", Firmware = "bios" });

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

    private sealed class RecordingQmpClient : IQmpClient
    {
        public List<string> Commands { get; } = [];

        public bool IsConnected => true;

        public IObservable<QmpEvent> Events => throw new NotSupportedException();

        public Task ConnectAsync(QmpEndpoint endpoint, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<JsonElement> ExecuteAsync(string command, object? arguments = null, CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            using var empty = JsonDocument.Parse("{}");
            return Task.FromResult(empty.RootElement.Clone());
        }

        public Task<TResult> ExecuteAsync<TResult>(string command, object? arguments = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<QmpSchema> GetSchemaAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

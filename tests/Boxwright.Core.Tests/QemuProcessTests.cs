using Xunit;

namespace Boxwright.Core.Tests;

// CORE-7: QemuProcess supervision, tested via a fake long-lived process.
public class QemuProcessTests
{
    [Fact]
    public void Start_TransitionsToRunning_AndPassesLaunchRequest()
    {
        WithTempFolder((folder, logPath) =>
        {
            var launcher = new FakeProcessLauncher();
            using var qemu = new QemuProcess(launcher, "qemu-system-x86_64", ["-m", "2048"], folder, logPath);

            qemu.Start();

            Assert.Equal(QemuProcessState.Running, qemu.State);
            Assert.NotNull(launcher.LastRequest);
            Assert.Equal("qemu-system-x86_64", launcher.LastRequest!.Executable);
            Assert.Equal(folder, launcher.LastRequest.WorkingDirectory);
        });
    }

    [Fact]
    public void Output_IsWrittenToLogFile()
    {
        WithTempFolder((folder, logPath) =>
        {
            var launcher = new FakeProcessLauncher();
            using var qemu = new QemuProcess(launcher, "qemu", [], folder, logPath);
            qemu.Start();

            launcher.Last!.EmitOutput("char device redirected");
            launcher.Last.EmitOutput("VNC server running on 127.0.0.1");
            qemu.Dispose(); // close the log so it can be read back

            string log = File.ReadAllText(logPath);
            Assert.Contains("char device redirected", log, StringComparison.Ordinal);
            Assert.Contains("VNC server running on 127.0.0.1", log, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Start_WritesLaunchHeaderBeforeOutput()
    {
        WithTempFolder((folder, logPath) =>
        {
            var launcher = new FakeProcessLauncher();
            using var qemu = new QemuProcess(launcher, "qemu-system-x86_64",
                ["-accel", "whpx,kernel-irqchip=off", "-cpu", "Westmere"], folder, logPath, Accelerator.Whpx);
            qemu.Start();
            launcher.Last!.EmitOutput("VNC server running on 127.0.0.1");
            qemu.Dispose();

            string log = File.ReadAllText(logPath);
            Assert.Contains("=== Boxwright launch", log, StringComparison.Ordinal);
            Assert.Contains("Accelerator: Whpx", log, StringComparison.Ordinal);
            Assert.Contains("-accel whpx,kernel-irqchip=off", log, StringComparison.Ordinal);
            Assert.Contains("-cpu Westmere", log, StringComparison.Ordinal);
            Assert.True(
                log.IndexOf("Boxwright launch", StringComparison.Ordinal) <
                log.IndexOf("VNC server", StringComparison.Ordinal),
                "the launch header should precede QEMU output");
        });
    }

    [Fact]
    public void Exit_SetsStateAndExitCode_AndRaisesExited()
    {
        WithTempFolder((folder, logPath) =>
        {
            var launcher = new FakeProcessLauncher();
            using var qemu = new QemuProcess(launcher, "qemu", [], folder, logPath);
            bool exitedRaised = false;
            qemu.Exited += (_, _) => exitedRaised = true;
            qemu.Start();

            launcher.Last!.SimulateExit(0);

            Assert.Equal(QemuProcessState.Exited, qemu.State);
            Assert.Equal(0, qemu.ExitCode);
            Assert.True(exitedRaised);
        });
    }

    [Fact]
    public void Start_WhenAlreadyStarted_Throws()
    {
        WithTempFolder((folder, logPath) =>
        {
            var launcher = new FakeProcessLauncher();
            using var qemu = new QemuProcess(launcher, "qemu", [], folder, logPath);
            qemu.Start();

            Assert.Throws<InvalidOperationException>(qemu.Start);
        });
    }

    [Fact]
    public async Task WaitForExitAsync_CompletesWhenProcessExits()
    {
        await WithTempFolderAsync(async (folder, logPath) =>
        {
            var launcher = new FakeProcessLauncher();
            using var qemu = new QemuProcess(launcher, "qemu", [], folder, logPath);
            qemu.Start();

            Task wait = qemu.WaitForExitAsync();
            launcher.Last!.SimulateExit(0);
            await wait;

            Assert.True(wait.IsCompletedSuccessfully);
        });
    }

    private static void WithTempFolder(Action<string, string> body)
    {
        string folder = Path.Combine(Path.GetTempPath(), $"boxwright-proc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            body(folder, Path.Combine(folder, "qemu.log"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static async Task WithTempFolderAsync(Func<string, string, Task> body)
    {
        string folder = Path.Combine(Path.GetTempPath(), $"boxwright-proc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            await body(folder, Path.Combine(folder, "qemu.log"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}

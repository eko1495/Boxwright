using System.Diagnostics;
using Boxwright.Core;
using Microsoft.Extensions.Logging.Abstractions;

// Boxwright end-to-end harness — MANUAL / on-demand (not run by `dotnet test`).
// Exercises the real Boxwright.Core lifecycle against a REAL QEMU install — the
// integration that the fake-based unit tests cannot cover:
//
//   detect accelerator -> create config + qcow2 disk -> launch qemu-system-x86_64
//   -> connect QMP -> pause/resume/reset -> graceful + force stop.
//
// Requires qemu-system-x86_64 + qemu-img (on PATH, or the default Windows install
// dir). Skips cleanly (exit 0) when QEMU is absent. A QEMU window may appear briefly.

var locator = new QemuLocator();

// Gate: skip gracefully when there is no real QEMU to drive.
try
{
    locator.ResolveSystemEmulator("x86_64");
}
catch (QemuNotFoundException)
{
    Console.WriteLine(@"SKIP: qemu-system-x86_64 not found (PATH or C:\Program Files\qemu). This harness needs a real QEMU install.");
    return 0;
}

int failures = 0;
void Check(string label, bool ok)
{
    Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {label}");
    if (!ok)
    {
        failures++;
    }
}

string root = Path.Combine(Path.GetTempPath(), "boxwright-e2e-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
Console.WriteLine($"Boxwright E2E (real QEMU). Root: {root}");

var stopwatch = Stopwatch.StartNew();
IRunningVm? running = null;
string vmFolder = string.Empty;
try
{
    var detector = AcceleratorDetector.CreateDefault();
    Accelerator accelerator = detector.Detect();
    Console.WriteLine($"Step 1: accelerator = {accelerator.ToQemuValue()}; qemu = {locator.ResolveSystemEmulator("x86_64")}");

    var repository = new VmRepository(root);
    var disks = new DiskService(new ProcessRunner(), locator);
    Vm vm = await repository.CreateAsync(new VmConfig
    {
        Name = "e2e",
        MemoryMiB = 1024,
        Cpu = new CpuConfig { Sockets = 1, Cores = 2, Threads = 1 },
        Disks = [new DiskConfig { File = "disk.qcow2", Format = "qcow2", Interface = "virtio" }],
    });
    vmFolder = vm.FolderPath;
    await disks.CreateAsync(Path.Combine(vm.FolderPath, "disk.qcow2"), 1L * 1024 * 1024 * 1024);
    Check("VM config + qcow2 disk created", File.Exists(vm.ConfigPath) && File.Exists(Path.Combine(vm.FolderPath, "disk.qcow2")));

    Console.WriteLine("Step 2: launch qemu-system-x86_64 + connect QMP");
    var launcher = new VmLauncher(new ProcessLauncher(), new QmpEndpointAllocator(), new DefaultQmpConnector(NullLogger<DefaultQmpConnector>.Instance), new DefaultQgaConnector(), detector, locator, NullLogger<VmLauncher>.Instance);
    running = await launcher.StartAsync(vm);
    Check("QEMU running + QMP connected", running.State == QemuProcessState.Running);
    Console.WriteLine($"        accelerator in use = {running.Accelerator.ToQemuValue()}, SPICE port = {running.SpicePort}");

    Console.WriteLine("Step 3: QMP pause -> resume -> reset");
    await running.PauseAsync();
    await running.ResumeAsync();
    await running.ResetAsync();
    Check("QMP control accepted; still running", running.State == QemuProcessState.Running);

    Console.WriteLine("Step 4: stop (graceful, force after 3s grace)");
    await running.StopAsync(TimeSpan.FromSeconds(3));
    // Kill() is async; the process-exit event propagates shortly after StopAsync returns.
    for (int i = 0; i < 30 && running.State != QemuProcessState.Exited; i++)
    {
        await Task.Delay(100);
    }

    Check("QEMU stopped", running.State == QemuProcessState.Exited);
}
catch (Exception ex) when (ex is not OperationCanceledException)
{
    Console.WriteLine($"  [FAIL] {ex.GetType().Name}: {ex.Message}");
    failures++;

    string logPath = Path.Combine(vmFolder, "qemu.log");
    if (File.Exists(logPath))
    {
        Console.WriteLine("--- qemu.log (QEMU stdout/stderr) ---");
        Console.WriteLine(File.ReadAllText(logPath).Trim());
        Console.WriteLine("--- end qemu.log ---");
    }
}
finally
{
    if (running is not null)
    {
        try
        {
            await running.DisposeAsync();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // Best-effort teardown.
        }
    }

    try
    {
        Directory.Delete(root, recursive: true);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        // Best-effort cleanup.
    }
}

stopwatch.Stop();
Console.WriteLine();
Console.WriteLine(failures == 0
    ? $"ALL PASSED in {stopwatch.ElapsedMilliseconds} ms"
    : $"{failures} FAILURE(S) in {stopwatch.ElapsedMilliseconds} ms");
return failures == 0 ? 0 : 1;

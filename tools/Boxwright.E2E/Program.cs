using System.Diagnostics;
using System.Security.Cryptography;
using Boxwright.Core;
using Microsoft.Extensions.Logging.Abstractions;

// Boxwright manual harness — on-demand (NOT run by `dotnet test`). Modes:
//
//   vm                              (default) real QEMU VM lifecycle: detect accelerator ->
//                                   create config + qcow2 disk -> launch qemu-system-x86_64 ->
//                                   connect QMP -> pause/resume/reset -> graceful + force stop.
//   download <url> <sha256> [dir]   exercise the REAL ISO downloader (HttpClient + SHA-256
//                                   verification + cache reuse) against a (small) file.
//   hash <url>                      stream a URL and print its SHA-256 + size — to author or
//                                   re-verify an OsCatalog.json entry.

string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "vm";
return mode switch
{
    "download" => await RunDownloadAsync(),
    "hash" => await RunHashAsync(),
    "vm" => await RunVmLifecycleAsync(),
    _ => Usage(),
};

int Usage()
{
    Console.WriteLine("Usage: dotnet run --project tools/Boxwright.E2E -- [vm | download <url> <sha256> [dir] | hash <url>]");
    return 2;
}

async Task<int> RunDownloadAsync()
{
    if (args.Length < 3)
    {
        Console.WriteLine("download needs: <url> <sha256-lowercase-hex> [destDir]");
        return 2;
    }

    string url = args[1];
    string sha = args[2];
    string dir = args.Length > 3 ? args[3] : Path.Combine(Path.GetTempPath(), "boxwright-dl-" + Guid.NewGuid().ToString("N"));

    var entry = new OsCatalogEntry
    {
        Id = "e2e-download",
        Name = "manual download",
        Version = "n/a",
        IsoUrl = new Uri(url),
        Sha256 = sha,
        SourceName = "manual",
    };

    using var http = new HttpClient();
    var downloader = new IsoDownloader(new HttpClientStreamSource(http), dir);
    var progress = new SyncProgress(p =>
        Console.Write($"\r  {Humanize(p.BytesReceived)} / {(p.TotalBytes is { } t ? Humanize(t) : "?")} ({(p.Percent ?? 0):0}%)          "));

    Console.WriteLine($"Downloading {url}");
    Console.WriteLine($"  expected sha256: {sha}");
    Console.WriteLine($"  cache dir:       {dir}");
    var sw = Stopwatch.StartNew();
    try
    {
        string path = await downloader.EnsureAsync(entry, progress);
        sw.Stop();
        Console.WriteLine();
        Console.WriteLine($"  [PASS] downloaded + SHA-256 verified in {sw.ElapsedMilliseconds} ms");
        Console.WriteLine($"         -> {path} ({Humanize(new FileInfo(path).Length)})");

        var sw2 = Stopwatch.StartNew();
        await downloader.EnsureAsync(entry);
        sw2.Stop();
        Console.WriteLine($"  [PASS] second call reused the cache in {sw2.ElapsedMilliseconds} ms (no re-download)");
        return 0;
    }
    catch (DownloadException ex)
    {
        Console.WriteLine();
        Console.WriteLine($"  [FAIL] {ex.Message}");
        return 1;
    }
}

async Task<int> RunHashAsync()
{
    if (args.Length < 2)
    {
        Console.WriteLine("hash needs: <url>");
        return 2;
    }

    string url = args[1];
    using var http = new HttpClient();
    using HttpDownload download = await new HttpClientStreamSource(http).OpenReadAsync(new Uri(url));
    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    byte[] buffer = new byte[1 << 20];
    long total = 0;
    int read;
    while ((read = await download.Content.ReadAsync(buffer)) > 0)
    {
        hash.AppendData(buffer, 0, read);
        total += read;
        Console.Write($"\r  {Humanize(total)}          ");
    }

    Console.WriteLine();
    Console.WriteLine($"  url    : {url}");
    Console.WriteLine($"  size   : {total} bytes ({Humanize(total)})");
    Console.WriteLine($"  sha256 : {Convert.ToHexStringLower(hash.GetHashAndReset())}");
    return 0;
}

async Task<int> RunVmLifecycleAsync()
{
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
        var launcher = new VmLauncher(new ProcessLauncher(), new QmpEndpointAllocator(), new DefaultQmpConnector(NullLogger<DefaultQmpConnector>.Instance), new DefaultQgaConnector(), new VmRuntimeStore(), detector, locator, NullLogger<VmLauncher>.Instance);
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
}

static string Humanize(long bytes)
{
    const double gb = 1_000_000_000d;
    const double mb = 1_000_000d;
    const double kb = 1_000d;
    return bytes >= gb ? $"{bytes / gb:0.00} GB"
        : bytes >= mb ? $"{bytes / mb:0.0} MB"
        : $"{bytes / kb:0.0} KB";
}

sealed class SyncProgress(Action<IsoDownloadProgress> callback) : IProgress<IsoDownloadProgress>
{
    public void Report(IsoDownloadProgress value) => callback(value);
}

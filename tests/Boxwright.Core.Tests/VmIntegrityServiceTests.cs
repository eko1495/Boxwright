using Xunit;

namespace Boxwright.Core.Tests;

// DiskService.CheckAsync maps qemu-img check exit codes; VmIntegrityService spans a VM's qcow2 disks.
public sealed class DiskServiceCheckTests
{
    private const string HealthyJson = "{\"corruptions\":0,\"leaks\":0,\"check-errors\":0}";
    private const string CorruptJson = "{\"corruptions\":3,\"leaks\":0,\"check-errors\":0}";
    private const string LeaksJson = "{\"corruptions\":0,\"leaks\":5,\"check-errors\":0}";

    [Fact]
    public async Task Exit0_parses_a_healthy_result()
    {
        await WithStubQemuImgAsync(async locator =>
        {
            var service = new DiskService(new FakeProcessRunner(exitCode: 0, standardOutput: HealthyJson), locator);

            DiskCheckResult result = await service.CheckAsync("disk.qcow2");

            Assert.True(result.Healthy);
            Assert.Equal(0, result.Corruptions);
        });
    }

    [Fact]
    public async Task Exit2_parses_a_corrupted_result_without_throwing()
    {
        await WithStubQemuImgAsync(async locator =>
        {
            var service = new DiskService(new FakeProcessRunner(exitCode: 2, standardOutput: CorruptJson), locator);

            DiskCheckResult result = await service.CheckAsync("disk.qcow2");

            Assert.False(result.Healthy);
            Assert.Equal(3, result.Corruptions);
        });
    }

    [Fact]
    public async Task Exit3_is_leaks_only_and_still_healthy()
    {
        await WithStubQemuImgAsync(async locator =>
        {
            var service = new DiskService(new FakeProcessRunner(exitCode: 3, standardOutput: LeaksJson), locator);

            DiskCheckResult result = await service.CheckAsync("disk.qcow2");

            Assert.True(result.Healthy); // leaks are wasted space, not corruption
            Assert.Equal(5, result.Leaks);
        });
    }

    [Fact]
    public async Task Exit63_unsupported_format_throws()
    {
        await WithStubQemuImgAsync(async locator =>
        {
            var service = new DiskService(new FakeProcessRunner(exitCode: 63, standardError: "does not support checks"), locator);

            await Assert.ThrowsAsync<DiskException>(() => service.CheckAsync("disk.raw"));
        });
    }

    private static async Task WithStubQemuImgAsync(Func<QemuLocator, Task> body)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"boxwright-check-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, OperatingSystem.IsWindows() ? "qemu-img.exe" : "qemu-img"), "stub");
        try
        {
            await body(new QemuLocator(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

public sealed class VmIntegrityServiceTests
{
    private static Vm VmWith(params DiskConfig[] disks) => new("/vms/vm1", new VmConfig { Name = "vm1", Disks = disks });

    private static DiskConfig Disk(string file, string format = "qcow2") => new() { File = file, Format = format };

    [Fact]
    public async Task Checks_every_qcow2_disk_and_skips_raw()
    {
        var fake = new FakeDiskService();
        fake.Results[Path.Combine("/vms/vm1", "os.qcow2")] = new DiskCheckResult();
        var service = new VmIntegrityService(fake);

        VmIntegrityReport report = await service.CheckAsync(
            VmWith(Disk("os.qcow2"), new DiskConfig { File = "seed.img", Format = "raw" }));

        DiskIntegrity only = Assert.Single(report.Disks); // raw seed skipped
        Assert.Equal("os.qcow2", only.File);
        Assert.True(report.Healthy);
        Assert.True(report.Checked);
    }

    [Fact]
    public async Task A_corrupted_disk_makes_the_report_unhealthy()
    {
        var fake = new FakeDiskService();
        fake.Results[Path.Combine("/vms/vm1", "os.qcow2")] = new DiskCheckResult();
        fake.Results[Path.Combine("/vms/vm1", "data.qcow2")] = new DiskCheckResult { Corruptions = 2 };
        var service = new VmIntegrityService(fake);

        VmIntegrityReport report = await service.CheckAsync(VmWith(Disk("os.qcow2"), Disk("data.qcow2")));

        Assert.False(report.Healthy);
        Assert.Equal(2, report.Disks.Single(d => d.File == "data.qcow2").Result!.Corruptions);
    }

    [Fact]
    public async Task A_disk_whose_check_fails_is_recorded_as_an_error_not_thrown()
    {
        var fake = new FakeDiskService();
        fake.Throw.Add(Path.Combine("/vms/vm1", "os.qcow2"));
        var service = new VmIntegrityService(fake);

        VmIntegrityReport report = await service.CheckAsync(VmWith(Disk("os.qcow2")));

        DiskIntegrity only = Assert.Single(report.Disks);
        Assert.NotNull(only.Error);
        Assert.Null(only.Result);
        Assert.False(report.Healthy); // an unchecked disk isn't "healthy"
    }

    [Fact]
    public async Task A_vm_with_only_raw_disks_reports_nothing_checked()
    {
        VmIntegrityReport report = await new VmIntegrityService(new FakeDiskService())
            .CheckAsync(VmWith(new DiskConfig { File = "seed.img", Format = "raw" }));

        Assert.False(report.Checked);
        Assert.False(report.Healthy);
        Assert.Empty(report.Disks);
    }

    private sealed class FakeDiskService : IDiskService
    {
        public Dictionary<string, DiskCheckResult> Results { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Throw { get; } = new(StringComparer.Ordinal);

        public Task<DiskCheckResult> CheckAsync(string path, CancellationToken cancellationToken = default)
        {
            if (Throw.Contains(path))
            {
                throw new DiskException($"qemu-img check failed for {path}");
            }

            return Task.FromResult(Results.TryGetValue(path, out DiskCheckResult? r) ? r : new DiskCheckResult());
        }

        public Task<DiskInfo> GetInfoAsync(string path, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task CreateAsync(string path, long sizeBytes, string format = "qcow2", CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ResizeAsync(string path, long sizeBytes, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task CopyAsync(string sourcePath, string destinationPath, string format = "qcow2", CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task CreateOverlayAsync(string backingPath, string overlayPath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RebaseAsync(string imagePath, string newBackingPath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

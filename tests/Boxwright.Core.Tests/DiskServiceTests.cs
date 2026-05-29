using Xunit;

namespace Boxwright.Core.Tests;

// CORE-6: DiskService wraps qemu-img, tested via FakeProcessRunner (no real qemu-img).
public class DiskServiceTests
{
    [Fact]
    public async Task CreateAsync_InvokesQemuImgCreate()
    {
        await WithStubQemuImgAsync(async locator =>
        {
            var fake = new FakeProcessRunner(exitCode: 0);
            var service = new DiskService(fake, locator);

            await service.CreateAsync("disk.qcow2", 42949672960L);

            (string FileName, IReadOnlyList<string> Arguments) invocation = Assert.Single(fake.Invocations);
            Assert.Equal("create -f qcow2 disk.qcow2 42949672960", string.Join(' ', invocation.Arguments));
        });
    }

    [Fact]
    public async Task CreateAsync_NonZeroExit_ThrowsDiskException()
    {
        await WithStubQemuImgAsync(async locator =>
        {
            var fake = new FakeProcessRunner(exitCode: 1, standardError: "qemu-img: could not create");
            var service = new DiskService(fake, locator);

            DiskException ex = await Assert.ThrowsAsync<DiskException>(() => service.CreateAsync("disk.qcow2", 1024));
            Assert.Contains("could not create", ex.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task CreateAsync_RejectsNonPositiveSize()
    {
        await WithStubQemuImgAsync(async locator =>
        {
            var service = new DiskService(new FakeProcessRunner(0), locator);

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.CreateAsync("disk.qcow2", 0));
        });
    }

    [Fact]
    public async Task GetInfoAsync_ParsesQemuImgJson()
    {
        const string infoJson =
            "{\"virtual-size\":42949672960,\"filename\":\"disk.qcow2\",\"format\":\"qcow2\",\"actual-size\":200704}";

        await WithStubQemuImgAsync(async locator =>
        {
            var fake = new FakeProcessRunner(exitCode: 0, standardOutput: infoJson);
            var service = new DiskService(fake, locator);

            DiskInfo info = await service.GetInfoAsync("disk.qcow2");

            Assert.Equal("disk.qcow2", info.Filename);
            Assert.Equal("qcow2", info.Format);
            Assert.Equal(42949672960L, info.VirtualSize);
            Assert.Equal(200704L, info.ActualSize);

            (string FileName, IReadOnlyList<string> Arguments) invocation = Assert.Single(fake.Invocations);
            Assert.Equal("info --output=json disk.qcow2", string.Join(' ', invocation.Arguments));
        });
    }

    [Fact]
    public async Task GetInfoAsync_NonZeroExit_ThrowsDiskException()
    {
        await WithStubQemuImgAsync(async locator =>
        {
            var fake = new FakeProcessRunner(exitCode: 1, standardError: "qemu-img: No such file");
            var service = new DiskService(fake, locator);

            await Assert.ThrowsAsync<DiskException>(() => service.GetInfoAsync("missing.qcow2"));
        });
    }

    private static async Task WithStubQemuImgAsync(Func<QemuLocator, Task> body)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"boxwright-disk-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string stub = Path.Combine(dir, OperatingSystem.IsWindows() ? "qemu-img.exe" : "qemu-img");
        await File.WriteAllTextAsync(stub, "stub");
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

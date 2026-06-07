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

    [Fact]
    public async Task ResizeAsync_InvokesQemuImgResize()
    {
        await WithStubQemuImgAsync(async locator =>
        {
            var fake = new FakeProcessRunner(exitCode: 0);
            var service = new DiskService(fake, locator);

            await service.ResizeAsync("disk.qcow2", 42949672960L);

            (string FileName, IReadOnlyList<string> Arguments) invocation = Assert.Single(fake.Invocations);
            Assert.Equal("resize disk.qcow2 42949672960", string.Join(' ', invocation.Arguments));
        });
    }

    [Fact]
    public async Task ResizeAsync_NonZeroExit_ThrowsDiskException()
    {
        await WithStubQemuImgAsync(async locator =>
        {
            var fake = new FakeProcessRunner(exitCode: 1, standardError: "qemu-img: shrink not allowed");
            var service = new DiskService(fake, locator);

            await Assert.ThrowsAsync<DiskException>(() => service.ResizeAsync("disk.qcow2", 1024));
        });
    }

    [Fact]
    public async Task ResizeAsync_RejectsNonPositiveSize()
    {
        await WithStubQemuImgAsync(async locator =>
        {
            var service = new DiskService(new FakeProcessRunner(0), locator);

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.ResizeAsync("disk.qcow2", 0));
        });
    }

    [Fact]
    public async Task CopyAsync_InvokesQemuImgConvert()
    {
        await WithStubQemuImgAsync(async locator =>
        {
            var fake = new FakeProcessRunner(exitCode: 0);
            var service = new DiskService(fake, locator);

            await service.CopyAsync("src.qcow2", "dst.qcow2");

            (string FileName, IReadOnlyList<string> Arguments) invocation = Assert.Single(fake.Invocations);
            Assert.Equal("convert -O qcow2 src.qcow2 dst.qcow2", string.Join(' ', invocation.Arguments));
        });
    }

    [Fact]
    public async Task CreateOverlayAsync_InvokesQemuImgCreateWithBacking()
    {
        await WithStubQemuImgAsync(async locator =>
        {
            var fake = new FakeProcessRunner(exitCode: 0);
            var service = new DiskService(fake, locator);

            await service.CreateOverlayAsync("base.qcow2", "overlay.qcow2");

            (string FileName, IReadOnlyList<string> Arguments) invocation = Assert.Single(fake.Invocations);
            Assert.Equal("create -f qcow2 -b base.qcow2 -F qcow2 overlay.qcow2", string.Join(' ', invocation.Arguments));
        });
    }

    [Fact]
    public async Task GetInfoAsync_ParsesBackingFilenames()
    {
        const string infoJson =
            "{\"filename\":\"overlay.qcow2\",\"format\":\"qcow2\",\"virtual-size\":1024,\"actual-size\":512," +
            "\"backing-filename\":\"base.qcow2\",\"full-backing-filename\":\"C:\\\\VMs\\\\a\\\\base.qcow2\"}";

        await WithStubQemuImgAsync(async locator =>
        {
            var fake = new FakeProcessRunner(exitCode: 0, standardOutput: infoJson);
            var service = new DiskService(fake, locator);

            DiskInfo info = await service.GetInfoAsync("overlay.qcow2");

            Assert.Equal("base.qcow2", info.BackingFilename);
            Assert.Equal("C:\\VMs\\a\\base.qcow2", info.FullBackingFilename);
        });
    }

    [Fact]
    public async Task RebaseAsync_InvokesQemuImgRebaseInSafeMode()
    {
        await WithStubQemuImgAsync(async locator =>
        {
            var fake = new FakeProcessRunner(exitCode: 0);
            var service = new DiskService(fake, locator);

            await service.RebaseAsync("child.qcow2", "parent.qcow2");

            (string FileName, IReadOnlyList<string> Arguments) invocation = Assert.Single(fake.Invocations);
            Assert.Equal("rebase -b parent.qcow2 -F qcow2 child.qcow2", string.Join(' ', invocation.Arguments));
            Assert.DoesNotContain("-u", invocation.Arguments); // never unsafe mode
        });
    }

    [Fact]
    public async Task RebaseAsync_NonZeroExit_ThrowsDiskException()
    {
        await WithStubQemuImgAsync(async locator =>
        {
            var fake = new FakeProcessRunner(exitCode: 1, standardError: "qemu-img: rebase failed");
            var service = new DiskService(fake, locator);

            await Assert.ThrowsAsync<DiskException>(() => service.RebaseAsync("child.qcow2", "parent.qcow2"));
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

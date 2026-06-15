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
        await WithStubQemuImgAsync(async (locator, dir) =>
        {
            var fake = new FakeProcessRunner(exitCode: 0);
            var service = new DiskService(fake, locator);
            string src = Path.Combine(dir, "src.qcow2");
            string dst = Path.Combine(dir, "dst.qcow2");
            await File.WriteAllTextAsync(src, "img");

            await service.CopyAsync(src, dst);

            (string FileName, IReadOnlyList<string> Arguments) invocation = Assert.Single(fake.Invocations);
            Assert.Equal(["convert", "-O", "qcow2", src, dst], invocation.Arguments);
        });
    }

    [Fact]
    public async Task CopyAsync_MissingSource_ThrowsDiskExceptionWithoutInvokingQemuImg()
    {
        await WithStubQemuImgAsync(async (locator, dir) =>
        {
            var fake = new FakeProcessRunner(exitCode: 0);
            var service = new DiskService(fake, locator);
            string src = Path.Combine(dir, "nope.qcow2");

            DiskException ex = await Assert.ThrowsAsync<DiskException>(() => service.CopyAsync(src, Path.Combine(dir, "dst.qcow2")));

            Assert.Contains(src, ex.Message, StringComparison.Ordinal);
            Assert.Empty(fake.Invocations); // failed fast, never spawned qemu-img
        });
    }

    [Fact]
    public async Task CreateOverlayAsync_InvokesQemuImgCreateWithBacking()
    {
        await WithStubQemuImgAsync(async (locator, dir) =>
        {
            var fake = new FakeProcessRunner(exitCode: 0);
            var service = new DiskService(fake, locator);
            string backing = Path.Combine(dir, "base.qcow2");
            string overlay = Path.Combine(dir, "overlay.qcow2");
            await File.WriteAllTextAsync(backing, "img");

            await service.CreateOverlayAsync(backing, overlay);

            (string FileName, IReadOnlyList<string> Arguments) invocation = Assert.Single(fake.Invocations);
            Assert.Equal(["create", "-f", "qcow2", "-b", backing, "-F", "qcow2", overlay], invocation.Arguments);
        });
    }

    [Fact]
    public async Task CreateOverlayAsync_MissingBacking_ThrowsDiskExceptionWithoutInvokingQemuImg()
    {
        await WithStubQemuImgAsync(async (locator, dir) =>
        {
            var fake = new FakeProcessRunner(exitCode: 0);
            var service = new DiskService(fake, locator);
            string backing = Path.Combine(dir, "gone.qcow2");

            DiskException ex = await Assert.ThrowsAsync<DiskException>(() => service.CreateOverlayAsync(backing, Path.Combine(dir, "overlay.qcow2")));

            Assert.Contains(backing, ex.Message, StringComparison.Ordinal);
            Assert.Empty(fake.Invocations);
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
        await WithStubQemuImgAsync(async (locator, dir) =>
        {
            var fake = new FakeProcessRunner(exitCode: 0);
            var service = new DiskService(fake, locator);
            string child = Path.Combine(dir, "child.qcow2");
            string parent = Path.Combine(dir, "parent.qcow2");
            await File.WriteAllTextAsync(child, "img");
            await File.WriteAllTextAsync(parent, "img");

            await service.RebaseAsync(child, parent);

            (string FileName, IReadOnlyList<string> Arguments) invocation = Assert.Single(fake.Invocations);
            Assert.Equal(["rebase", "-b", parent, "-F", "qcow2", child], invocation.Arguments);
            Assert.DoesNotContain("-u", invocation.Arguments); // never unsafe mode
        });
    }

    [Fact]
    public async Task RebaseAsync_NonZeroExit_ThrowsDiskException()
    {
        await WithStubQemuImgAsync(async (locator, dir) =>
        {
            var fake = new FakeProcessRunner(exitCode: 1, standardError: "qemu-img: rebase failed");
            var service = new DiskService(fake, locator);
            string child = Path.Combine(dir, "child.qcow2");
            string parent = Path.Combine(dir, "parent.qcow2");
            await File.WriteAllTextAsync(child, "img");
            await File.WriteAllTextAsync(parent, "img");

            await Assert.ThrowsAsync<DiskException>(() => service.RebaseAsync(child, parent));
        });
    }

    private static Task WithStubQemuImgAsync(Func<QemuLocator, Task> body) =>
        WithStubQemuImgAsync((locator, _) => body(locator));

    // Overload that also hands the test the temp directory, so it can create real input image files
    // (the Copy/Overlay/Rebase paths now require their inputs to exist before invoking qemu-img).
    private static async Task WithStubQemuImgAsync(Func<QemuLocator, string, Task> body)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"boxwright-disk-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string stub = Path.Combine(dir, OperatingSystem.IsWindows() ? "qemu-img.exe" : "qemu-img");
        await File.WriteAllTextAsync(stub, "stub");
        try
        {
            await body(new QemuLocator(dir), dir);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

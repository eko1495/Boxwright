using Xunit;

namespace Boxwright.Core.Tests;

// Snapshots wrap qemu-img snapshot / info, tested via FakeProcessRunner (no real qemu-img).
public class SnapshotServiceTests
{
    [Fact]
    public async Task CreateAsync_InvokesQemuImgSnapshotCreate()
    {
        await WithStubQemuImgAsync(async locator =>
        {
            var fake = new FakeProcessRunner(exitCode: 0);
            var service = new SnapshotService(fake, locator);

            await service.CreateAsync("disk.qcow2", "before-update");

            (string FileName, IReadOnlyList<string> Arguments) invocation = Assert.Single(fake.Invocations);
            Assert.Equal("snapshot -c before-update disk.qcow2", string.Join(' ', invocation.Arguments));
        });
    }

    [Fact]
    public async Task RestoreAndDelete_UseTheCorrectQemuImgFlags()
    {
        await WithStubQemuImgAsync(async locator =>
        {
            var fake = new FakeProcessRunner(exitCode: 0);
            var service = new SnapshotService(fake, locator);

            await service.RestoreAsync("disk.qcow2", "snap1");
            await service.DeleteAsync("disk.qcow2", "snap1");

            Assert.Equal("snapshot -a snap1 disk.qcow2", string.Join(' ', fake.Invocations[0].Arguments));
            Assert.Equal("snapshot -d snap1 disk.qcow2", string.Join(' ', fake.Invocations[1].Arguments));
        });
    }

    [Fact]
    public async Task ListAsync_ParsesSnapshotsFromQemuImgInfoJson()
    {
        const string infoJson =
            "{\"format\":\"qcow2\",\"snapshots\":[" +
            "{\"id\":\"1\",\"name\":\"before-update\",\"vm-state-size\":0,\"date-sec\":1700000000}]}";

        await WithStubQemuImgAsync(async locator =>
        {
            var fake = new FakeProcessRunner(exitCode: 0, standardOutput: infoJson);
            var service = new SnapshotService(fake, locator);

            IReadOnlyList<VmSnapshot> snapshots = await service.ListAsync("disk.qcow2");

            VmSnapshot snapshot = Assert.Single(snapshots);
            Assert.Equal("before-update", snapshot.Name);
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), snapshot.Created);
            Assert.Equal("info --output=json disk.qcow2", string.Join(' ', fake.Invocations[0].Arguments));
        });
    }

    [Fact]
    public async Task ListAsync_WithNoSnapshots_ReturnsEmpty()
    {
        await WithStubQemuImgAsync(async locator =>
        {
            var fake = new FakeProcessRunner(exitCode: 0, standardOutput: "{\"format\":\"qcow2\"}");
            var service = new SnapshotService(fake, locator);

            Assert.Empty(await service.ListAsync("disk.qcow2"));
        });
    }

    [Fact]
    public async Task CreateAsync_NonZeroExit_ThrowsDiskException()
    {
        await WithStubQemuImgAsync(async locator =>
        {
            var fake = new FakeProcessRunner(exitCode: 1, standardError: "qemu-img: snapshot failed");
            var service = new SnapshotService(fake, locator);

            DiskException ex = await Assert.ThrowsAsync<DiskException>(() => service.CreateAsync("disk.qcow2", "x"));
            Assert.Contains("snapshot failed", ex.Message, StringComparison.Ordinal);
        });
    }

    private static async Task WithStubQemuImgAsync(Func<QemuLocator, Task> body)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"boxwright-snap-{Guid.NewGuid():N}");
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

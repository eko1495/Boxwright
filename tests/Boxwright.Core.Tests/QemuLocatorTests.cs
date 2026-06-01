using Xunit;

namespace Boxwright.Core.Tests;

// CORE-2: QemuLocator path resolution.
public class QemuLocatorTests
{
    [Fact]
    public void ResolveImageTool_FindsBinaryInBundledDirectory()
    {
        string bundled = CreateTempDir();
        try
        {
            string expected = Path.Combine(bundled, ExeName("qemu-img"));
            File.WriteAllText(expected, "stub");
            var locator = new QemuLocator(bundled);

            string resolved = locator.ResolveImageTool();

            Assert.Equal(expected, resolved);
        }
        finally
        {
            Directory.Delete(bundled, recursive: true);
        }
    }

    [Fact]
    public void ResolveSystemEmulator_FindsBundledBinary()
    {
        string bundled = CreateTempDir();
        try
        {
            string expected = Path.Combine(bundled, ExeName("qemu-system-x86_64"));
            File.WriteAllText(expected, "stub");
            var locator = new QemuLocator(bundled);

            string resolved = locator.ResolveSystemEmulator("x86_64");

            Assert.Equal(expected, resolved);
        }
        finally
        {
            Directory.Delete(bundled, recursive: true);
        }
    }

    [Fact]
    public void Resolve_WhenBinaryMissingEverywhere_ThrowsQemuNotFound()
    {
        // A bogus arch guarantees no such binary on PATH or in any default location.
        var locator = new QemuLocator(bundledDirectory: null);

        Assert.Throws<QemuNotFoundException>(() => locator.ResolveSystemEmulator("totally-fake-arch"));
    }

    [Fact]
    public void ResolveSystemEmulator_RejectsBlankArch()
    {
        var locator = new QemuLocator();

        Assert.ThrowsAny<ArgumentException>(() => locator.ResolveSystemEmulator(""));
    }

    [Fact]
    public void ResolveUefiFirmware_FindsCodeAndVarsInTheShareFolder()
    {
        string bundled = CreateTempDir();
        string share = Path.Combine(bundled, "share");
        Directory.CreateDirectory(share);
        try
        {
            File.WriteAllText(Path.Combine(bundled, ExeName("qemu-system-x86_64")), "stub");
            File.WriteAllText(Path.Combine(share, "edk2-x86_64-code.fd"), "code");
            File.WriteAllText(Path.Combine(share, "edk2-i386-vars.fd"), "vars");
            var locator = new QemuLocator(bundled);

            UefiFirmware firmware = locator.ResolveUefiFirmware("x86_64");

            Assert.Equal(Path.Combine(share, "edk2-x86_64-code.fd"), firmware.CodePath);
            Assert.Equal(Path.Combine(share, "edk2-i386-vars.fd"), firmware.VarsTemplatePath);
        }
        finally
        {
            Directory.Delete(bundled, recursive: true);
        }
    }

    private static string ExeName(string baseName) =>
        OperatingSystem.IsWindows() ? baseName + ".exe" : baseName;

    private static string CreateTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"boxwright-qemu-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }
}

using System.Text;
using Boxwright.Core;
using DiscUtils.Iso9660;
using Xunit;

namespace Boxwright.Core.Tests;

// The file-extraction path (CDReader pulling /casper/vmlinuz + initrd) is verified live against a real
// Ubuntu ISO — DiscUtils' own CDBuilder mangles extension-less names (the ADR-0013 trailing-dot issue), so
// a synthetic ISO can't faithfully stand in for one. Here we unit-test the pure command-line composition
// and the "this isn't an Ubuntu live ISO" guard.
public sealed class InstallMediaExtractorTests
{
    private const string SeedArgs = "autoinstall ds=nocloud";

    [Fact]
    public void BuildAppend_PrependsSeedArgs_AndPreservesTheIsoKernelCmdline()
    {
        const string grub = """
            menuentry "Try or Install Ubuntu" {
                set gfxpayload=keep
                linux   /casper/vmlinuz layerfs-path=minimal.standard.live.squashfs --- quiet splash
                initrd  /casper/initrd
            }
            """;

        string append = InstallMediaExtractor.BuildAppend(SeedArgs, grub);

        Assert.Equal("autoinstall ds=nocloud layerfs-path=minimal.standard.live.squashfs --- quiet splash", append);
    }

    [Theory]
    [InlineData("")]
    [InlineData("menuentry {\n  set foo=bar\n}")] // no `linux /casper/vmlinuz` line
    public void BuildAppend_NoMatchingKernelLine_ReturnsSeedArgsOnly(string grub)
    {
        Assert.Equal(SeedArgs, InstallMediaExtractor.BuildAppend(SeedArgs, grub));
    }

    [Fact]
    public void Extract_NotAnUbuntuLiveIso_Throws()
    {
        using var temp = new TempDir();
        var builder = new CDBuilder { UseJoliet = true, VolumeIdentifier = "OTHER" };
        builder.AddFile("readme.txt", Encoding.UTF8.GetBytes("not ubuntu"));
        string iso = Path.Combine(temp.Path, "other.iso");
        builder.Build(iso);

        var extractor = new InstallMediaExtractor();
        Assert.Throws<InstallMediaException>(() => extractor.Extract(iso, Path.Combine(temp.Path, "vm"), SeedArgs));
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bw-extract-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}

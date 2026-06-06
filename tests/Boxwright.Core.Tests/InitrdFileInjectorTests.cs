using System.IO.Compression;
using System.Text;
using Boxwright.Core;
using Xunit;

namespace Boxwright.Core.Tests;

public sealed class InitrdFileInjectorTests
{
    [Theory]
    [InlineData("preseed.cfg", "d-i debian-installer/locale string en_US.UTF-8\n")] // Debian
    [InlineData("ks.cfg", "lang en_US.UTF-8\nrootpw --lock\npoweroff\n")]            // Fedora kickstart
    public void Append_WritesAGzippedCpioSegment_CarryingTheFile(string fileName, string content)
    {
        using var temp = new TempFile();

        InitrdFileInjector.Append(temp.Path, fileName, content);

        // The file is exactly our appended gzip segment (the initrd started empty). Decompress + parse it.
        byte[] cpio = Gunzip(File.ReadAllBytes(temp.Path));
        (string name, byte[] data) = FirstCpioEntry(cpio);

        Assert.Equal(fileName, name);
        Assert.Equal(content, Encoding.UTF8.GetString(data));
        Assert.Contains("TRAILER!!!", Encoding.ASCII.GetString(cpio), StringComparison.Ordinal);
    }

    [Fact]
    public void Append_PreservesTheOriginalInitrd_AndGrowsIt()
    {
        using var temp = new TempFile();
        byte[] original = Encoding.ASCII.GetBytes("ORIGINAL-INITRD-BYTES");
        File.WriteAllBytes(temp.Path, original);

        InitrdFileInjector.Append(temp.Path, "ks.cfg", "lang en_US.UTF-8\n");

        byte[] result = File.ReadAllBytes(temp.Path);
        Assert.True(result.Length > original.Length);
        Assert.Equal(original, result[..original.Length]); // the original bytes are untouched at the front
    }

    private static byte[] Gunzip(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        using var gz = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gz.CopyTo(output);
        return output.ToArray();
    }

    // Parses the first entry of a cpio "newc" archive (110-byte ASCII header, then NUL-terminated name
    // padded to 4 bytes, then file data padded to 4 bytes).
    private static (string Name, byte[] Data) FirstCpioEntry(byte[] cpio)
    {
        string header = Encoding.ASCII.GetString(cpio, 0, 110);
        Assert.StartsWith("070701", header, StringComparison.Ordinal); // newc magic

        int fileSize = Convert.ToInt32(header.Substring(54, 8), 16);
        int nameSize = Convert.ToInt32(header.Substring(94, 8), 16);

        string name = Encoding.ASCII.GetString(cpio, 110, nameSize - 1); // drop the trailing NUL
        int dataStart = Align4(110 + nameSize);
        byte[] data = cpio[dataStart..(dataStart + fileSize)];
        return (name, data);
    }

    private static int Align4(int value) => (value + 3) & ~3;

    private sealed class TempFile : IDisposable
    {
        public TempFile() => Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bw-initrd-" + Guid.NewGuid().ToString("N"));

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                File.Delete(Path);
            }
            catch (IOException)
            {
            }
        }
    }
}

using Boxwright.Core;
using DiscUtils.Fat;
using Xunit;

namespace Boxwright.Core.Tests;

public sealed class CloudInitSeedGeneratorTests
{
    [Fact]
    public void Generate_WritesSeedImageIntoTheVmFolder()
    {
        using var temp = new TempDir();
        var generator = new CloudInitSeedGenerator();

        string path = generator.Generate(new UnattendedAnswers { Password = "pw" }, temp.Path);

        Assert.True(File.Exists(path));
        Assert.Equal(CloudInitSeedGenerator.SeedFileName, Path.GetFileName(path));
        Assert.Equal(temp.Path, Path.GetDirectoryName(path));
    }

    [Fact]
    public void Generate_ProducesCidataFatImageWithUserDataAndMetaData()
    {
        using var temp = new TempDir();
        var generator = new CloudInitSeedGenerator();
        var answers = new UnattendedAnswers { Hostname = "vm1", Username = "alice", Password = "secret" };

        string path = generator.Generate(answers, temp.Path);

        // Round-trip: re-open the image and confirm cloud-init will find what it needs, by the EXACT
        // names (a FAT volume preserves "user-data"/"meta-data", unlike the ISO9660 writer).
        using var image = new MemoryStream(File.ReadAllBytes(path));
        using var fs = new FatFileSystem(image);

        Assert.StartsWith(CloudInitSeedGenerator.VolumeLabel, fs.VolumeLabel);
        Assert.True(fs.FileExists("user-data"));
        Assert.True(fs.FileExists("meta-data"));

        string userData = ReadAllText(fs, "user-data");
        Assert.StartsWith("#cloud-config", userData);
        Assert.Contains("hostname: vm1", userData);
        Assert.Contains("username: alice", userData);

        Assert.Contains("instance-id:", ReadAllText(fs, "meta-data"));
    }

    private static string ReadAllText(FatFileSystem fs, string name)
    {
        using Stream stream = fs.OpenFile(name, FileMode.Open, FileAccess.Read);
        using var text = new StreamReader(stream);
        return text.ReadToEnd();
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bw-seed-" + Guid.NewGuid().ToString("N"));
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

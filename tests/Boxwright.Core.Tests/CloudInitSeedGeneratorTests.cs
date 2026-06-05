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

    [Fact]
    public void Generate_CloudImageProfile_WritesPlainCloudConfig_NotAutoinstall()
    {
        using var temp = new TempDir();
        var generator = new CloudInitSeedGenerator();
        var answers = new UnattendedAnswers { Username = "alice", Password = "secret" };

        string path = generator.Generate(answers, temp.Path, SeedProfile.CloudImage);

        using var image = new MemoryStream(File.ReadAllBytes(path));
        using var fs = new FatFileSystem(image);

        string userData = ReadAllText(fs, "user-data");
        Assert.StartsWith("#cloud-config", userData);
        Assert.DoesNotContain("autoinstall:", userData); // the cloud-image profile must not emit subiquity config
        Assert.Contains("name: alice", userData);
    }

    [Fact]
    public void Generate_WritesARootDirectoryVolumeLabelEntry_SoLinuxUdevExposesTheLabel()
    {
        // Regression (found by an end-to-end QEMU smoke test): DiscUtils writes the label only into the
        // BPB boot sector, which Linux blkid exposes as LABEL_FATBOOT — but udev creates
        // /dev/disk/by-label/CIDATA only from the real LABEL (a root-directory volume-label entry,
        // attribute 0x08). Without that entry cloud-init's NoCloud probe never finds the seed. Assert the
        // generator writes it. (FormatFloppy always produces FAT12 with a fixed-size root directory.)
        using var temp = new TempDir();
        string path = new CloudInitSeedGenerator().Generate(new UnattendedAnswers { Password = "pw" }, temp.Path);
        byte[] img = File.ReadAllBytes(path);

        int bytesPerSector = img[11] | (img[12] << 8);
        int reservedSectors = img[14] | (img[15] << 8);
        int fatCount = img[16];
        int rootEntryCount = img[17] | (img[18] << 8);
        int sectorsPerFat = img[22] | (img[23] << 8);
        long rootDir = (long)(reservedSectors + (fatCount * sectorsPerFat)) * bytesPerSector;

        string? labelName = null;
        for (int i = 0; i < rootEntryCount; i++)
        {
            int e = (int)rootDir + (i * 32);
            if (img[e] == 0x00)
            {
                break; // end of directory
            }

            int attr = img[e + 11];
            bool isLongName = (attr & 0x0F) == 0x0F;
            if (!isLongName && (attr & 0x08) != 0) // ATTR_VOLUME_ID, and not an LFN entry
            {
                labelName = System.Text.Encoding.ASCII.GetString(img, e, 11).TrimEnd();
                break;
            }
        }

        Assert.Equal(CloudInitSeedGenerator.VolumeLabel, labelName);
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

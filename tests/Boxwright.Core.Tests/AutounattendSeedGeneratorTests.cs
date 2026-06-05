using Boxwright.Core;
using DiscUtils.Iso9660;
using Xunit;

namespace Boxwright.Core.Tests;

public sealed class AutounattendSeedGeneratorTests
{
    [Fact]
    public void Generate_WritesAnIsoIntoTheVmFolder()
    {
        using var temp = new TempDir();
        var generator = new AutounattendSeedGenerator();

        string path = generator.Generate(
            new UnattendedAnswers { Username = "alice", Password = "pw" },
            new WindowsInstallOptions(),
            uefi: true,
            temp.Path);

        Assert.True(File.Exists(path));
        Assert.Equal(AutounattendSeedGenerator.SeedFileName, Path.GetFileName(path));
        Assert.Equal(temp.Path, Path.GetDirectoryName(path));
    }

    [Fact]
    public void Generate_PutsAutounattendXmlAtTheIsoRoot_ReadableViaJoliet()
    {
        using var temp = new TempDir();
        var generator = new AutounattendSeedGenerator();
        var answers = new UnattendedAnswers { Hostname = "winbox", Username = "alice", Password = "pw" };

        string path = generator.Generate(answers, new WindowsInstallOptions(), uefi: true, temp.Path);

        // Round-trip: re-open the ISO and confirm Windows Setup will find Autounattend.xml at the root.
        using FileStream iso = File.OpenRead(path);
        var reader = new CDReader(iso, joliet: true);
        Assert.True(reader.FileExists(AutounattendXml.FileName));

        using Stream file = reader.OpenFile(AutounattendXml.FileName, FileMode.Open);
        using var text = new StreamReader(file);
        string xml = text.ReadToEnd();
        Assert.Contains("urn:schemas-microsoft-com:unattend", xml);
        Assert.Contains("<ComputerName>winbox</ComputerName>", xml);
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bw-autounattend-" + Guid.NewGuid().ToString("N"));
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

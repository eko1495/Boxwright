using Boxwright.Core;
using Xunit;

namespace Boxwright.Core.Tests;

public sealed class AutoinstallUserDataTests
{
    [Fact]
    public void Build_StartsWithCloudConfigAndDeclaresAutoinstall()
    {
        string ud = AutoinstallUserData.Build(new UnattendedAnswers { Password = "pw" });

        Assert.StartsWith("#cloud-config", ud);
        Assert.Contains("autoinstall:", ud);
        Assert.Contains("version: 1", ud);
    }

    [Fact]
    public void Build_EmbedsHashedPassword_NeverPlaintext()
    {
        string ud = AutoinstallUserData.Build(new UnattendedAnswers { Username = "alice", Password = "hunter2" });

        Assert.DoesNotContain("hunter2", ud);    // the plaintext must not leak into the seed
        Assert.Contains("password: \"$6$", ud);  // a SHA-512 crypt hash is embedded instead
    }

    [Fact]
    public void Build_SubstitutesAllAnswers()
    {
        string ud = AutoinstallUserData.Build(new UnattendedAnswers
        {
            Hostname = "myhost",
            Username = "bob",
            Password = "pw",
            Locale = "en_GB.UTF-8",
            Timezone = "Europe/Warsaw",
            KeyboardLayout = "pl",
        });

        Assert.Contains("hostname: myhost", ud);
        Assert.Contains("username: bob", ud);
        Assert.Contains("locale: en_GB.UTF-8", ud);
        Assert.Contains("timezone: Europe/Warsaw", ud);
        Assert.Contains("layout: pl", ud);
    }

    [Fact]
    public void Build_PowersOffWhenInstallFinishes()
    {
        // So the VM doesn't reboot straight back into the installer (boot order "dc").
        string ud = AutoinstallUserData.Build(new UnattendedAnswers { Password = "pw" });

        Assert.Contains("shutdown -P now", ud);
    }

    [Fact]
    public void Build_InstallsOntoLargestDisk_NotTheTinySeed()
    {
        // The seed is attached as a tiny disk; "match: size: largest" keeps the installer off it.
        string ud = AutoinstallUserData.Build(new UnattendedAnswers { Password = "pw" });

        Assert.Contains("size: largest", ud);
    }

    [Fact]
    public void MetaData_ContainsInstanceId()
    {
        Assert.Contains("instance-id: abc-123", AutoinstallUserData.MetaData("abc-123"));
    }
}

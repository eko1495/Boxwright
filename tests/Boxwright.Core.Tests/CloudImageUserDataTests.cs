using Boxwright.Core;
using Xunit;

namespace Boxwright.Core.Tests;

public sealed class CloudImageUserDataTests
{
    [Fact]
    public void Build_IsPlainCloudConfig_WithNoAutoinstallBlock()
    {
        // A cloud image is already installed — it must NOT carry a subiquity autoinstall block.
        string ud = CloudImageUserData.Build(new UnattendedAnswers { Password = "pw" });

        Assert.StartsWith("#cloud-config", ud);
        Assert.DoesNotContain("autoinstall:", ud);
    }

    [Fact]
    public void Build_DefinesASudoUserWithAnUnlockedPassword()
    {
        string ud = CloudImageUserData.Build(new UnattendedAnswers { Username = "alice", Password = "pw" });

        Assert.Contains("name: alice", ud);
        Assert.Contains("lock_passwd: false", ud); // the password actually lets you log in
        Assert.Contains("sudo: ALL=(ALL) NOPASSWD:ALL", ud);
    }

    [Fact]
    public void Build_EmbedsHashedPassword_NeverPlaintext()
    {
        string ud = CloudImageUserData.Build(new UnattendedAnswers { Username = "alice", Password = "hunter2" });

        Assert.DoesNotContain("hunter2", ud);     // the plaintext must not leak into the seed
        Assert.Contains("passwd: \"$6$", ud);     // a SHA-512 crypt hash is embedded instead
    }

    [Fact]
    public void Build_SubstitutesHostnameLocaleTimezoneAndKeyboard()
    {
        string ud = CloudImageUserData.Build(new UnattendedAnswers
        {
            Hostname = "myhost",
            Username = "bob",
            Password = "pw",
            Locale = "en_GB.UTF-8",
            Timezone = "Europe/Warsaw",
            KeyboardLayout = "pl",
        });

        Assert.Contains("hostname: myhost", ud);
        Assert.Contains("locale: en_GB.UTF-8", ud);
        Assert.Contains("timezone: Europe/Warsaw", ud);
        Assert.Contains("layout: pl", ud);
    }
}

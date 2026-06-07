using Boxwright.Core;
using Xunit;

namespace Boxwright.Core.Tests;

public sealed class DebianPreseedTests
{
    private static UnattendedAnswers Answers() => new()
    {
        Hostname = "boxwright-deb",
        Username = "alice",
        Password = "s3cr3t-pw",
        Locale = "en_US.UTF-8",
        Timezone = "Europe/Warsaw",
        KeyboardLayout = "us",
    };

    [Fact]
    public void Build_SubstitutesAnswers()
    {
        string preseed = DebianPreseed.Build(Answers());

        Assert.Contains("d-i netcfg/get_hostname string boxwright-deb", preseed, StringComparison.Ordinal);
        Assert.Contains("d-i passwd/username string alice", preseed, StringComparison.Ordinal);
        Assert.Contains("d-i debian-installer/locale string en_US.UTF-8", preseed, StringComparison.Ordinal);
        Assert.Contains("d-i time/zone string Europe/Warsaw", preseed, StringComparison.Ordinal);
        Assert.Contains("d-i keyboard-configuration/xkb-keymap select us", preseed, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_EmbedsACryptHash_NotThePlaintextPassword()
    {
        string preseed = DebianPreseed.Build(Answers());

        Assert.Contains("d-i passwd/user-password-crypted password $6$", preseed, StringComparison.Ordinal);
        Assert.DoesNotContain("s3cr3t-pw", preseed, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_InstallsTheGnomeDesktop()
    {
        string preseed = DebianPreseed.Build(Answers());

        Assert.Contains("tasksel tasksel/first multiselect standard, gnome-desktop", preseed, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("d-i partman/confirm boolean true")]
    [InlineData("d-i partman/confirm_nooverwrite boolean true")]
    [InlineData("d-i partman-partitioning/confirm_write_new_label boolean true")]
    [InlineData("d-i partman/choose_partition select finish")]
    [InlineData("d-i partman-lvm/confirm boolean true")]
    public void Build_AutoConfirmsEveryDiskWritePrompt(string directive)
    {
        // These are Debian's equivalent of subiquity's "Review your choices" gate — all must be preseeded.
        Assert.Contains(directive, DebianPreseed.Build(Answers()), StringComparison.Ordinal);
    }

    [Fact]
    public void Build_PowersOffAtTheEnd_SoTheInstallGraduates()
    {
        Assert.Contains("d-i debian-installer/exit/poweroff boolean true", DebianPreseed.Build(Answers()), StringComparison.Ordinal);
    }
}

using Boxwright.Core;
using Xunit;

namespace Boxwright.Core.Tests;

public sealed class FedoraKickstartTests
{
    private static UnattendedAnswers Answers() => new()
    {
        Hostname = "boxwright-fed",
        Username = "alice",
        Password = "s3cr3t-pw",
        Locale = "en_US.UTF-8",
        Timezone = "Europe/Warsaw",
        KeyboardLayout = "us",
    };

    [Fact]
    public void Build_SubstitutesAnswers()
    {
        string ks = FedoraKickstart.Build(Answers());

        Assert.Contains("network --bootproto=dhcp --hostname=boxwright-fed", ks, StringComparison.Ordinal);
        Assert.Contains("lang en_US.UTF-8", ks, StringComparison.Ordinal);
        Assert.Contains("timezone Europe/Warsaw --utc", ks, StringComparison.Ordinal);
        Assert.Contains("keyboard --vckeymap=us", ks, StringComparison.Ordinal);
        Assert.Contains("--name=alice", ks, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_EmbedsACryptHash_NotThePlaintextPassword()
    {
        string ks = FedoraKickstart.Build(Answers());

        Assert.Contains("--iscrypted --password=$6$", ks, StringComparison.Ordinal);
        Assert.DoesNotContain("s3cr3t-pw", ks, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_InstallsTheWorkstationEnvironment_FromTheMirrors()
    {
        string ks = FedoraKickstart.Build(Answers());

        Assert.Contains("@^workstation-product-environment", ks, StringComparison.Ordinal);
        Assert.Contains("url --mirrorlist=", ks, StringComparison.Ordinal); // netinst has no packages
    }

    [Theory]
    [InlineData("clearpart --all --initlabel")]
    [InlineData("autopart")]
    [InlineData("rootpw --lock")]
    [InlineData("poweroff")] // powers off at the end so the VM graduates
    public void Build_AutomatesEveryStep(string directive)
    {
        Assert.Contains(directive, FedoraKickstart.Build(Answers()), StringComparison.Ordinal);
    }

    [Fact]
    public void BuildAppend_ReusesTheIsosOwnStage2Label_WhenPresent()
    {
        const string grub = """
            menuentry 'Install Fedora 44' {
                linuxefi /images/pxeboot/vmlinuz inst.stage2=hd:LABEL=Fedora-E-dvd-x86_64-44 quiet
                initrdefi /images/pxeboot/initrd.img
            }
            """;

        string append = FedoraKickstartInstaller.BuildAppend(grub, "IGNORED-FALLBACK-LABEL");

        Assert.Contains("inst.ks=file:/ks.cfg", append, StringComparison.Ordinal);
        Assert.Contains("inst.stage2=hd:LABEL=Fedora-E-dvd-x86_64-44", append, StringComparison.Ordinal);
        Assert.DoesNotContain("IGNORED-FALLBACK-LABEL", append, StringComparison.Ordinal);
        Assert.Contains("inst.text", append, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildAppend_FallsBackToTheVolumeLabel_WhenGrubHasNoStage2()
    {
        string append = FedoraKickstartInstaller.BuildAppend(grubCfgContent: "", isoVolumeLabel: "Fedora-WS x64");

        Assert.Contains(@"inst.stage2=hd:LABEL=Fedora-WS\x20x64", append, StringComparison.Ordinal); // space escaped
        Assert.Contains("inst.ks=file:/ks.cfg", append, StringComparison.Ordinal);
    }
}

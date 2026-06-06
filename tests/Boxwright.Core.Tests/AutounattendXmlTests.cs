using System.Text;
using System.Xml.Linq;
using Boxwright.Core;
using Xunit;

namespace Boxwright.Core.Tests;

public sealed class AutounattendXmlTests
{
    private static UnattendedAnswers Answers() => new()
    {
        Hostname = "winbox",
        Username = "alice",
        Password = "hunter2",
    };

    [Fact]
    public void EncodePassword_MatchesMicrosoftsVerifiedVector()
    {
        // Base64(UTF-16LE("pw" + "Password")); verified against Microsoft's own LocalAccount sample.
        Assert.Equal("cAB3AFAAYQBzAHMAdwBvAHIAZAA=", AutounattendXml.EncodePassword("pw", "Password"));
    }

    [Fact]
    public void Build_IsWellFormedXml_InTheUnattendNamespace()
    {
        string xml = AutounattendXml.Build(Answers(), new WindowsInstallOptions(), uefi: true);

        XDocument doc = XDocument.Parse(xml); // throws if malformed
        Assert.Equal("unattend", doc.Root!.Name.LocalName);
        Assert.Equal("urn:schemas-microsoft-com:unattend", doc.Root.Name.NamespaceName);
        Assert.StartsWith("<?xml", xml);
    }

    [Fact]
    public void Build_HasTheThreeSetupPasses()
    {
        string xml = AutounattendXml.Build(Answers(), new WindowsInstallOptions(), uefi: true);

        Assert.Contains("<settings pass=\"windowsPE\">", xml);
        Assert.Contains("<settings pass=\"specialize\">", xml);
        Assert.Contains("<settings pass=\"oobeSystem\">", xml);
    }

    [Fact]
    public void Build_PowersOffWhenInstallFinishes_ViaFirstLogonShutdown()
    {
        // The Autounattend shuts the guest down at first logon so QEMU exits and Boxwright graduates the
        // VM (eject media, disk-first boot) — the Windows analogue of the Linux seed's poweroff (ADR-0015).
        string xml = AutounattendXml.Build(Answers(), new WindowsInstallOptions(), uefi: true);

        XNamespace ns = "urn:schemas-microsoft-com:unattend";
        var commandLines = XDocument.Parse(xml)
            .Descendants(ns + "FirstLogonCommands")
            .Descendants(ns + "CommandLine")
            .Select(e => e.Value)
            .ToList();

        Assert.Contains(commandLines, c =>
            c.Contains("shutdown", StringComparison.OrdinalIgnoreCase) && c.Contains("/s", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_CreatesTheAdminAccount_ViaSpecializePass_AndAutoLogsOn()
    {
        string xml = AutounattendXml.Build(Answers(), new WindowsInstallOptions(), uefi: true);

        Assert.Contains("<ComputerName>winbox</ComputerName>", xml);
        // The account is created in the specialize pass (which ConX honours), not oobeSystem LocalAccounts.
        Assert.Contains("net user \"alice\"", xml);
        Assert.Contains("net localgroup \"Administrators\" \"alice\"", xml);
        Assert.Contains("AutoAdminLogon", xml);
        Assert.DoesNotContain("<LocalAccount", xml); // the oobeSystem LocalAccount was removed
        Assert.Contains("<Enabled>true</Enabled>", xml); // oobeSystem AutoLogon (legacy path)
    }

    [Fact]
    public void Build_OobeAutoLogon_UsesTheObfuscatedPassword()
    {
        string xml = AutounattendXml.Build(Answers(), new WindowsInstallOptions(), uefi: true);

        // The oobeSystem AutoLogon value is the Base64(UTF-16LE(pw+"Password")) obfuscation.
        string encoded = AutounattendXml.EncodePassword("hunter2", "Password");
        Assert.Contains($"<Value>{encoded}</Value>", xml);
        Assert.Equal("hunter2Password", Encoding.Unicode.GetString(Convert.FromBase64String(encoded)));
    }

    [Fact]
    public void Build_SpecializeAccountScript_CarriesThePasswordInPlaintext_ConxTradeoff()
    {
        // The specialize `net user` + Winlogon DefaultPassword script needs the password in plaintext
        // (ADR-0015) — a documented tradeoff for ConX (24H2/25H2) compatibility. Setup strips it
        // post-install, and the oobeSystem value was only Base64 obfuscation anyway.
        string xml = AutounattendXml.Build(Answers(), new WindowsInstallOptions(), uefi: true);

        Assert.Contains("net user \"alice\" \"hunter2\"", xml);
        Assert.Contains("BypassNRO", xml);
    }

    [Fact]
    public void Build_BypassesWindows11HardwareChecks()
    {
        string xml = AutounattendXml.Build(Answers(), new WindowsInstallOptions(), uefi: true);

        Assert.Contains("BypassTPMCheck", xml);
        Assert.Contains("BypassSecureBootCheck", xml);
        Assert.Contains("BypassRAMCheck", xml);
    }

    [Fact]
    public void Build_Uefi_UsesGptLayout_InstallingToPartition3()
    {
        string xml = AutounattendXml.Build(Answers(), new WindowsInstallOptions(), uefi: true);

        Assert.Contains("<Type>EFI</Type>", xml);
        Assert.Contains("<Type>MSR</Type>", xml);
        Assert.Contains("<PartitionID>3</PartitionID>", xml); // InstallTo the Windows partition
        Assert.DoesNotContain("<Active>true</Active>", xml);   // Active is MBR-only
    }

    [Fact]
    public void Build_Bios_UsesMbrLayout_SingleActivePartition()
    {
        string xml = AutounattendXml.Build(Answers(), new WindowsInstallOptions(), uefi: false);

        Assert.Contains("<Active>true</Active>", xml);
        Assert.DoesNotContain("<Type>EFI</Type>", xml);
        Assert.Contains("<PartitionID>1</PartitionID>", xml); // InstallTo the single primary
    }

    [Fact]
    public void Build_OmitsProductKey_WhenNoneSupplied_ButIncludesItWhenGiven()
    {
        string withoutKey = AutounattendXml.Build(Answers(), new WindowsInstallOptions(), uefi: true);
        Assert.DoesNotContain("<ProductKey>", withoutKey); // Evaluation/single-edition ISOs need none

        string withKey = AutounattendXml.Build(
            Answers(), new WindowsInstallOptions { ProductKey = WindowsInstallOptions.GenericProKey }, uefi: true);
        Assert.Contains("<ProductKey>", withKey);
        Assert.Contains($"<Key>{WindowsInstallOptions.GenericProKey}</Key>", withKey);
    }

    [Fact]
    public void Build_EscapesUserSuppliedText()
    {
        var answers = Answers() with { Hostname = "a<b&c" };

        string xml = AutounattendXml.Build(answers, new WindowsInstallOptions(), uefi: true);

        Assert.DoesNotContain("<ComputerName>a<b&c", xml);   // raw, unescaped
        Assert.Contains("a&lt;b&amp;c", xml);                 // escaped
        XDocument.Parse(xml);                                 // still well-formed
    }
}

using System.Security;
using System.Text;

namespace Boxwright.Core;

/// <summary>
/// Builds a Windows Setup <c>Autounattend.xml</c> answer file for a fully unattended install from a
/// <see cref="UnattendedAnswers"/> + <see cref="WindowsInstallOptions"/>. The result wipes disk 0,
/// installs the chosen edition, bypasses the Windows 11 TPM/Secure-Boot/RAM checks (so it runs on a
/// plain QEMU q35 VM with no vTPM), creates a local administrator, and auto-logs-on so the first boot
/// lands on the desktop. Built from a fixed, verified template (see ADR-0015) — no XML library needed.
/// </summary>
public static class AutounattendXml
{
    /// <summary>The on-media file name Windows Setup auto-discovers at a removable-media root.</summary>
    public const string FileName = "Autounattend.xml";

    /// <summary>
    /// Encodes a password for an unattend <c>&lt;Password&gt;&lt;Value&gt;</c> with
    /// <c>&lt;PlainText&gt;false&lt;/PlainText&gt;</c>: <c>Base64(UTF-16LE(password + suffix))</c>. The
    /// suffix is <c>"Password"</c> for a LocalAccount/AutoLogon password and
    /// <c>"AdministratorPassword"</c> for the built-in Administrator. (Obfuscation, not encryption —
    /// Setup strips it from the on-disk copy after install.)
    /// </summary>
    public static string EncodePassword(string password, string suffix = "Password") =>
        Convert.ToBase64String(Encoding.Unicode.GetBytes((password ?? string.Empty) + suffix));

    /// <summary>Builds the <c>Autounattend.xml</c> document. <paramref name="uefi"/> selects the GPT (UEFI) vs MBR (BIOS) disk layout.</summary>
    public static string Build(UnattendedAnswers answers, WindowsInstallOptions options, bool uefi)
    {
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(options);

        string computerName = Esc(answers.Hostname);
        string userName = Esc(answers.Username);
        string passwordB64 = EncodePassword(answers.Password);
        string passwordPlain = Esc(answers.Password);
        string locale = Esc(options.Locale);
        string inputLocale = Esc(options.InputLocale);
        string timeZone = Esc(options.TimeZone);

        // GPT for UEFI (ESP + MSR + Windows = PartitionID 3); MBR for BIOS (one active primary = PartitionID 1).
        string diskConfiguration = uefi ? UefiDiskConfiguration : BiosDiskConfiguration;
        int installPartitionId = uefi ? 3 : 1;

        // Only emit a ProductKey when supplied — an Evaluation/single-edition ISO must NOT get one.
        string productKey = string.IsNullOrWhiteSpace(options.ProductKey)
            ? string.Empty
            : $"""
                        <ProductKey>
                          <Key>{Esc(options.ProductKey)}</Key>
                          <WillShowUI>OnError</WillShowUI>
                        </ProductKey>
              """;

        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <unattend xmlns="urn:schemas-microsoft-com:unattend"
                      xmlns:wcm="http://schemas.microsoft.com/WMIConfig/2002/State.xsd"
                      xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">

              <settings pass="windowsPE">
                <component name="Microsoft-Windows-International-Core-WinPE" processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS">
                  <SetupUILanguage>
                    <UILanguage>{locale}</UILanguage>
                  </SetupUILanguage>
                  <InputLocale>{inputLocale}</InputLocale>
                  <SystemLocale>{locale}</SystemLocale>
                  <UILanguage>{locale}</UILanguage>
                  <UserLocale>{locale}</UserLocale>
                </component>
                <component name="Microsoft-Windows-Setup" processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS">
                  <RunSynchronous>
                    <RunSynchronousCommand wcm:action="add"><Order>1</Order><Path>reg add HKLM\System\Setup\LabConfig /v BypassTPMCheck /t REG_DWORD /d 1 /f</Path></RunSynchronousCommand>
                    <RunSynchronousCommand wcm:action="add"><Order>2</Order><Path>reg add HKLM\System\Setup\LabConfig /v BypassSecureBootCheck /t REG_DWORD /d 1 /f</Path></RunSynchronousCommand>
                    <RunSynchronousCommand wcm:action="add"><Order>3</Order><Path>reg add HKLM\System\Setup\LabConfig /v BypassRAMCheck /t REG_DWORD /d 1 /f</Path></RunSynchronousCommand>
                    <RunSynchronousCommand wcm:action="add"><Order>4</Order><Path>reg add HKLM\System\Setup\LabConfig /v BypassStorageCheck /t REG_DWORD /d 1 /f</Path></RunSynchronousCommand>
                    <RunSynchronousCommand wcm:action="add"><Order>5</Order><Path>reg add HKLM\System\Setup\LabConfig /v BypassCPUCheck /t REG_DWORD /d 1 /f</Path></RunSynchronousCommand>
                    <RunSynchronousCommand wcm:action="add"><Order>6</Order><Path>reg add HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\OOBE /v BypassNRO /t REG_DWORD /d 1 /f</Path></RunSynchronousCommand>
                  </RunSynchronous>
            {diskConfiguration}
                  <ImageInstall>
                    <OSImage>
                      <InstallFrom>
                        <MetaData wcm:action="add">
                          <Key>/IMAGE/INDEX</Key>
                          <Value>{options.ImageIndex}</Value>
                        </MetaData>
                      </InstallFrom>
                      <InstallTo>
                        <DiskID>0</DiskID>
                        <PartitionID>{installPartitionId}</PartitionID>
                      </InstallTo>
                    </OSImage>
                  </ImageInstall>
                  <UserData>
            {productKey}
                    <AcceptEula>true</AcceptEula>
                    <FullName>{userName}</FullName>
                    <Organization></Organization>
                  </UserData>
                </component>
              </settings>

              <settings pass="specialize">
                <component name="Microsoft-Windows-Shell-Setup" processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS">
                  <ComputerName>{computerName}</ComputerName>
                  <TimeZone>{timeZone}</TimeZone>
                </component>
                <!-- Create the account + autologon + BypassNRO in the *specialize* pass: the Windows 11
                     24H2/25H2 "ConX" setup ignores the oobeSystem account, but honours specialize, so this
                     is the cross-setup-mode path (ADR-0015). Runs on legacy too. -->
                <component name="Microsoft-Windows-Deployment" processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS">
                  <RunSynchronous>
                    <RunSynchronousCommand wcm:action="add"><Order>1</Order><Path>cmd /c net user "{userName}" "{passwordPlain}" /add</Path></RunSynchronousCommand>
                    <RunSynchronousCommand wcm:action="add"><Order>2</Order><Path>cmd /c net localgroup "Administrators" "{userName}" /add</Path></RunSynchronousCommand>
                    <RunSynchronousCommand wcm:action="add"><Order>3</Order><Path>reg add "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" /v AutoAdminLogon /t REG_SZ /d 1 /f</Path></RunSynchronousCommand>
                    <RunSynchronousCommand wcm:action="add"><Order>4</Order><Path>reg add "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" /v DefaultUserName /t REG_SZ /d "{userName}" /f</Path></RunSynchronousCommand>
                    <RunSynchronousCommand wcm:action="add"><Order>5</Order><Path>reg add "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" /v DefaultPassword /t REG_SZ /d "{passwordPlain}" /f</Path></RunSynchronousCommand>
                    <RunSynchronousCommand wcm:action="add"><Order>6</Order><Path>reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\OOBE" /v BypassNRO /t REG_DWORD /d 1 /f</Path></RunSynchronousCommand>
                  </RunSynchronous>
                </component>
              </settings>

              <settings pass="oobeSystem">
                <component name="Microsoft-Windows-International-Core" processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS">
                  <InputLocale>{inputLocale}</InputLocale>
                  <SystemLocale>{locale}</SystemLocale>
                  <UILanguage>{locale}</UILanguage>
                  <UserLocale>{locale}</UserLocale>
                </component>
                <component name="Microsoft-Windows-Shell-Setup" processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS">
                  <OOBE>
                    <HideEULAPage>true</HideEULAPage>
                    <HideOEMRegistrationScreen>true</HideOEMRegistrationScreen>
                    <HideOnlineAccountScreens>true</HideOnlineAccountScreens>
                    <HideWirelessSetupInOOBE>true</HideWirelessSetupInOOBE>
                    <NetworkLocation>Work</NetworkLocation>
                    <ProtectYourPC>3</ProtectYourPC>
                    <SkipMachineOOBE>true</SkipMachineOOBE>
                    <SkipUserOOBE>true</SkipUserOOBE>
                  </OOBE>
                  <!-- The account is created in the specialize pass above (cross-setup-mode). AutoLogon here
                       references it; on a ConX setup that ignores oobeSystem, the specialize-pass Winlogon
                       registry provides autologon instead. -->
                  <AutoLogon>
                    <Enabled>true</Enabled>
                    <Username>{userName}</Username>
                    <LogonCount>1</LogonCount>
                    <Password>
                      <Value>{passwordB64}</Value>
                      <PlainText>false</PlainText>
                    </Password>
                  </AutoLogon>
                  <FirstLogonCommands>
                    <SynchronousCommand wcm:action="add">
                      <Order>1</Order>
                      <CommandLine>cmd /c shutdown /s /t 8 /c "Boxwright: install complete"</CommandLine>
                      <Description>Power off when the install completes so Boxwright finalizes the VM</Description>
                    </SynchronousCommand>
                  </FirstLogonCommands>
                </component>
              </settings>
            </unattend>

            """;
    }

    // Wipe disk 0; ESP (FAT32, 260 MB) + MSR (128 MB, unformatted) + Windows (NTFS, rest). Windows = PartitionID 3.
    private const string UefiDiskConfiguration = """
                  <DiskConfiguration>
                    <Disk wcm:action="add">
                      <DiskID>0</DiskID>
                      <WillWipeDisk>true</WillWipeDisk>
                      <CreatePartitions>
                        <CreatePartition wcm:action="add"><Order>1</Order><Type>EFI</Type><Size>260</Size></CreatePartition>
                        <CreatePartition wcm:action="add"><Order>2</Order><Type>MSR</Type><Size>128</Size></CreatePartition>
                        <CreatePartition wcm:action="add"><Order>3</Order><Type>Primary</Type><Extend>true</Extend></CreatePartition>
                      </CreatePartitions>
                      <ModifyPartitions>
                        <ModifyPartition wcm:action="add"><Order>1</Order><PartitionID>1</PartitionID><Label>System</Label><Format>FAT32</Format></ModifyPartition>
                        <ModifyPartition wcm:action="add"><Order>2</Order><PartitionID>3</PartitionID><Label>Windows</Label><Letter>C</Letter><Format>NTFS</Format></ModifyPartition>
                      </ModifyPartitions>
                    </Disk>
                    <WillShowUI>OnError</WillShowUI>
                  </DiskConfiguration>
            """;

    // Wipe disk 0; one active primary NTFS partition (MBR/BIOS). Windows = PartitionID 1.
    private const string BiosDiskConfiguration = """
                  <DiskConfiguration>
                    <Disk wcm:action="add">
                      <DiskID>0</DiskID>
                      <WillWipeDisk>true</WillWipeDisk>
                      <CreatePartitions>
                        <CreatePartition wcm:action="add"><Order>1</Order><Type>Primary</Type><Extend>true</Extend></CreatePartition>
                      </CreatePartitions>
                      <ModifyPartitions>
                        <ModifyPartition wcm:action="add"><Order>1</Order><PartitionID>1</PartitionID><Label>Windows</Label><Letter>C</Letter><Format>NTFS</Format><Active>true</Active></ModifyPartition>
                      </ModifyPartitions>
                    </Disk>
                    <WillShowUI>OnError</WillShowUI>
                  </DiskConfiguration>
            """;

    private static string Esc(string? value) => SecurityElement.Escape(value ?? string.Empty);
}

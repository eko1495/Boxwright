; Boxwright Windows installer (ADR-0029). Compiled by tools/package-windows.ps1 via:
;   ISCC.exe /DAppVersion=<ver> /DStagedDir=<staged publish tree> /O<output dir> packaging\windows-installer.iss
; The staged tree is the same one the ZIP is built from (ADR-0009). This script globs it
; wholesale, so QEMU pin bumps never require installer edits.

#ifndef AppVersion
  #error Pass /DAppVersion=<version>
#endif
#ifndef StagedDir
  #error Pass /DStagedDir=<absolute path to the staged publish tree>
#endif

[Setup]
; NEVER change AppId: it is how future setups find this install to upgrade in place.
AppId={{7C8E2F41-9B3A-4D65-A1E0-52C4B7D9F308}
AppName=Boxwright
AppVersion={#AppVersion}
AppPublisher=Boxwright project
AppSupportURL=https://github.com/eko1495/Boxwright
; Per-user, no UAC (project directive: no admin for the default path).
; {autopf} resolves to %LOCALAPPDATA%\Programs under PrivilegesRequired=lowest.
PrivilegesRequired=lowest
DefaultDirName={autopf}\Boxwright
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Restart Manager: interactive installs prompt the user to close Boxwright / running QEMU
; that hold files under {app} - never killed, never relaunched. NOTE: in silent installs
; (/SILENT, /VERYSILENT) Inno auto-closes in-use apps without a prompt - do NOT upgrade
; silently while a VM is running (revisit with a [Code] guard before any winget submission).
CloseApplications=yes
RestartApplications=no
OutputBaseFilename=Boxwright-Setup-{#AppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile={#SourcePath}..\src\Boxwright.App\Assets\boxwright.ico
UninstallDisplayIcon={app}\Boxwright.App.exe

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
; Upgrades: clear the previous QEMU tree first so files from an older weilnetz pin
; cannot linger next to the new one. App files are simply overwritten.
; If an app file or native dependency is ever renamed/removed between versions, add an
; [InstallDelete] entry for the old name - plain overwrite never removes vanished files.
Type: filesandordirs; Name: "{app}\qemu"

[Files]
Source: "{#StagedDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{autoprograms}\Boxwright"; Filename: "{app}\Boxwright.App.exe"
Name: "{autodesktop}\Boxwright"; Filename: "{app}\Boxwright.App.exe"; Tasks: desktopicon

[Code]
// Uninstall removes only the app; VM data survives. Tell the user where it lives.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
    MsgBox('Boxwright was removed.' + #13#10 + #13#10 +
           'Your virtual machines, logs and settings were left in place under:' + #13#10 +
           ExpandConstant('{localappdata}\Boxwright'), mbInformation, MB_OK);
end;

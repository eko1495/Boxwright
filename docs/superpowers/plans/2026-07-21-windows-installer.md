# Windows Installer (Inno Setup) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship `Boxwright-Setup-<version>-win-x64.exe` — a per-user, no-UAC Inno Setup installer built from the same staged tree as the existing ZIP, with in-place upgrades, VM-data-preserving uninstall, and a CI silent-install smoke test.

**Architecture:** `tools/package-windows.ps1` already stages a complete `publish/` tree (self-contained app + `qemu/` + notices) and zips it. We add `packaging/windows-installer.iss` that globs that same tree, a final ISCC step in the packaging script, a reusable smoke-test script, and wire both into `package-windows.yml`. Zero app-code changes — the installed layout is identical to the unzipped ZIP, so `QemuLocator` just works.

**Tech Stack:** Inno Setup 6 (ISCC), PowerShell 7, GitHub Actions (`windows-latest`).

**Spec:** `docs/superpowers/specs/2026-07-20-windows-installer-design.md` (approved). New ADR: **ADR-0022**, refining ADR-0009 (which stays Accepted; the ZIP keeps shipping).

## Global Constraints

- Per-user install, **no UAC prompt ever**: `PrivilegesRequired=lowest` → `%LOCALAPPDATA%\Programs\Boxwright`.
- **Never auto-kill QEMU** during upgrade — Restart Manager prompt only (`CloseApplications=yes`, `RestartApplications=no`).
- Uninstall must never touch `%LOCALAPPDATA%\Boxwright` (VMs, logs, settings).
- The `.iss` **globs** the staged tree — no per-file listing; QEMU pin bumps must need zero installer edits.
- `AppId` GUID `{7C8E2F41-9B3A-4D65-A1E0-52C4B7D9F308}` is permanent — never change it (it is how upgrades find the install).
- Setup filename: `Boxwright-Setup-<version>-win-x64.exe`; ZIP continues to ship alongside.
- Still unsigned — keep SmartScreen "More info → Run anyway" copy; performance copy stays honest (Directive 9).
- Out of scope: code signing, winget/Store, update checker, virt-viewer bundling, per-machine installs.
- PowerShell files follow the existing style of `tools/package-windows.ps1` (`#requires -Version 7`, `Set-StrictMode -Version Latest`, `$ErrorActionPreference = 'Stop'`).
- Run `dotnet format` is N/A (no C# changes); there must be **no C# changes** in this plan.

---

### Task 1: Installer script + smoke-test script

**Files:**
- Create: `packaging/windows-installer.iss`
- Create: `tools/test-windows-installer.ps1`

**Interfaces:**
- Consumes: a staged tree shaped like `artifacts/win-x64/publish` (contains `Boxwright.App.exe`, `qemu\`, `THIRD-PARTY-NOTICES.txt`), path passed as `/DStagedDir=`.
- Produces: `Boxwright-Setup-<AppVersion>-win-x64.exe` in the `/O<dir>` output dir. `tools/test-windows-installer.ps1 -SetupExe <path>` exits 0 on install+uninstall success, throws otherwise. Task 2 invokes ISCC with exactly: `& $iscc "/DAppVersion=$Version" "/DStagedDir=$publishDir" "/O$OutputDir" $issPath`.

- [ ] **Step 1: Ensure Inno Setup 6 is installed locally**

Run: `winget install -e --id JRSoftware.InnoSetup` (skip if already installed).
Verify: `Test-Path "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"` → `True`.

- [ ] **Step 2: Write the smoke-test script (the failing test)**

Create `tools/test-windows-installer.ps1`:

```powershell
#requires -Version 7
<#
.SYNOPSIS
  Smoke-tests a Boxwright setup EXE: silent per-user install, assert the expected layout,
  silent uninstall, assert removal. Used locally and by package-windows.yml.

  WARNING: really installs into (and then removes) %LOCALAPPDATA%\Programs\Boxwright on
  THIS machine - do not run on a machine with a Boxwright install you care about.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $SetupExe
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$installDir = Join-Path $env:LOCALAPPDATA 'Programs\Boxwright'
$silentArgs = '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART'

Write-Host "Installing $SetupExe ..."
$p = Start-Process -FilePath (Resolve-Path $SetupExe) -ArgumentList $silentArgs -Wait -PassThru
if ($p.ExitCode -ne 0) { throw "Setup exited with $($p.ExitCode)" }

foreach ($f in 'Boxwright.App.exe', 'qemu\qemu-system-x86_64.exe', 'THIRD-PARTY-NOTICES.txt', 'unins000.exe') {
    $path = Join-Path $installDir $f
    if (-not (Test-Path $path)) { throw "Missing after install: $path" }
}
Write-Host "Install layout OK: $installDir"

Write-Host 'Uninstalling ...'
$p = Start-Process -FilePath (Join-Path $installDir 'unins000.exe') -ArgumentList $silentArgs -Wait -PassThru
if ($p.ExitCode -ne 0) { throw "Uninstaller exited with $($p.ExitCode)" }

# unins000.exe hands off to a temp copy of itself; the folder disappears shortly after it returns.
$deadline = (Get-Date).AddSeconds(30)
while ((Test-Path $installDir) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
if (Test-Path $installDir) { throw "Install dir still present after uninstall: $installDir" }
Write-Host 'Uninstall OK.'
```

- [ ] **Step 3: Build a dummy staged tree and verify the test fails (no setup EXE yet)**

```powershell
$stage = 'artifacts\installer-test\publish'
New-Item -ItemType Directory -Force (Join-Path $stage 'qemu') | Out-Null
'dummy' | Set-Content (Join-Path $stage 'Boxwright.App.exe')
'dummy' | Set-Content (Join-Path $stage 'qemu\qemu-system-x86_64.exe')
Copy-Item packaging\THIRD-PARTY-NOTICES.txt $stage
pwsh tools\test-windows-installer.ps1 -SetupExe artifacts\installer-test\Boxwright-Setup-0.0.0-test-win-x64.exe
```

Expected: FAIL — `Resolve-Path` throws because the setup EXE does not exist.

- [ ] **Step 4: Write the Inno Setup script**

Create `packaging/windows-installer.iss`:

```ini
; Boxwright Windows installer (ADR-0022). Compiled by tools/package-windows.ps1 via:
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
; Restart Manager: prompt the user to close Boxwright / running QEMU that hold files
; under {app}. Never kill (a killed VM risks guest corruption), never relaunch.
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
```

- [ ] **Step 5: Compile against the dummy tree**

```powershell
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" `
    /DAppVersion=0.0.0-test `
    "/DStagedDir=$(Resolve-Path artifacts\installer-test\publish)" `
    /Oartifacts\installer-test `
    packaging\windows-installer.iss
```

Expected: `Successful compile` and `artifacts\installer-test\Boxwright-Setup-0.0.0-test-win-x64.exe` exists.

- [ ] **Step 6: Run the smoke test — verify it passes**

Run: `pwsh tools\test-windows-installer.ps1 -SetupExe artifacts\installer-test\Boxwright-Setup-0.0.0-test-win-x64.exe`
Expected: `Install layout OK`, `Uninstall OK.`, exit code 0.

- [ ] **Step 7: Verify upgrade-over-existing works**

Install, then install again (simulates a newer setup over an existing install), assert still intact, then uninstall:

```powershell
$setup = 'artifacts\installer-test\Boxwright-Setup-0.0.0-test-win-x64.exe'
Start-Process $setup -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -Wait
pwsh tools\test-windows-installer.ps1 -SetupExe $setup
```

Expected: the second run (install over install → uninstall) succeeds — exit 0, `Uninstall OK.`.

- [ ] **Step 8: Commit**

```powershell
git add packaging/windows-installer.iss tools/test-windows-installer.ps1
git commit -m "feat: Inno Setup installer script + silent install/uninstall smoke test (ADR-0022)"
```

---

### Task 2: Compile the installer from `package-windows.ps1`

**Files:**
- Modify: `tools/package-windows.ps1` (param block at lines 15–21; synopsis at lines 2–14; append a step 6 after the ZIP block that ends at line 106)

**Interfaces:**
- Consumes: `packaging/windows-installer.iss` from Task 1 with defines `AppVersion`, `StagedDir` and ISCC `/O`.
- Produces: `<OutputDir>\Boxwright-Setup-<Version>-win-x64.exe` (default OutputDir `artifacts\win-x64`). New switch `-SkipInstaller` skips the step. Task 3's workflow relies on both artifacts landing flat in `artifacts/win-x64/`.

- [ ] **Step 1: Add the `-SkipInstaller` switch and update the synopsis**

In the `param(...)` block, change:

```powershell
    [switch] $SkipPublish
```

to:

```powershell
    [switch] $SkipPublish,
    [switch] $SkipInstaller
```

In the `.SYNOPSIS` (line 4–5), change "and zips it." to "zips it, and compiles the Inno Setup installer (ADR-0022)."

- [ ] **Step 2: Add the ISCC locator + installer step at the end of the script**

Append after the final `Write-Host "SHA-256: $zipHash"` line:

```powershell
# 6. Installer (ADR-0022): compile the Inno Setup script over the same staged tree.
function Find-Iscc {
    $cmd = Get-Command 'iscc.exe' -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
    throw 'ISCC.exe (Inno Setup 6) not found. Install it (winget install -e --id JRSoftware.InnoSetup) or pass -SkipInstaller.'
}

if (-not $SkipInstaller) {
    $iscc = Find-Iscc
    $issPath = Join-Path $repoRoot 'packaging\windows-installer.iss'
    Write-Host ''
    Write-Host 'Compiling installer ...'
    & $iscc "/DAppVersion=$Version" "/DStagedDir=$publishDir" "/O$OutputDir" $issPath
    if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)" }
    $setupPath = Join-Path $OutputDir "Boxwright-Setup-$Version-win-x64.exe"
    if (-not (Test-Path $setupPath)) { throw "ISCC succeeded but $setupPath is missing" }
    $setupMB   = [math]::Round((Get-Item $setupPath).Length / 1MB, 1)
    $setupHash = (Get-FileHash -Algorithm SHA256 $setupPath).Hash
    Write-Host "Created $setupPath ($setupMB MB)"
    Write-Host "SHA-256: $setupHash"
}
```

- [ ] **Step 3: End-to-end local run with a fake QEMU dir (avoids the ~350 MB download)**

The script's step-3 bundle assertion requires exactly these four files, so fake them:

```powershell
$fakeQemu = 'artifacts\installer-test\fake-qemu'
New-Item -ItemType Directory -Force (Join-Path $fakeQemu 'share') | Out-Null
foreach ($f in 'qemu-system-x86_64.exe', 'qemu-img.exe', 'share\edk2-x86_64-code.fd', 'share\edk2-i386-vars.fd') {
    'dummy' | Set-Content (Join-Path $fakeQemu $f)
}
pwsh tools\package-windows.ps1 -Version 0.0.0-test -QemuDir (Resolve-Path $fakeQemu)
```

Expected: publish → qemu copy → zip → `Compiling installer ...` → `Created artifacts\win-x64\Boxwright-Setup-0.0.0-test-win-x64.exe (... MB)` with a SHA-256 line. (This does a real `dotnet publish`; takes a minute or two.)

- [ ] **Step 4: Smoke-test the real setup EXE**

Run: `pwsh tools\test-windows-installer.ps1 -SetupExe artifacts\win-x64\Boxwright-Setup-0.0.0-test-win-x64.exe`
Expected: `Install layout OK`, `Uninstall OK.`, exit 0.

- [ ] **Step 5: Verify `-SkipInstaller` still produces only the ZIP**

Run: `pwsh tools\package-windows.ps1 -Version 0.0.0-test -QemuDir (Resolve-Path artifacts\installer-test\fake-qemu) -SkipPublish -SkipInstaller`
Expected: completes with the ZIP `Created ...` lines and **no** `Compiling installer` line.

- [ ] **Step 6: Commit**

```powershell
git add tools/package-windows.ps1
git commit -m "feat: package-windows.ps1 compiles the Inno Setup installer (-SkipInstaller to opt out)"
```

---

### Task 3: CI — build, smoke-test, and release the installer

**Files:**
- Modify: `.github/workflows/package-windows.yml`

**Interfaces:**
- Consumes: `tools/package-windows.ps1` (Task 2) producing `artifacts/win-x64/Boxwright-Setup-<version>-win-x64.exe`; `tools/test-windows-installer.ps1` (Task 1).
- Produces: release/artifact uploads containing both the ZIP and the setup EXE.

- [ ] **Step 1: Update the workflow**

Change the header comment (lines 3–4) to:

```yaml
# Builds the self-contained Windows ZIP and the Inno Setup installer (app + bundled QEMU)
# on a version tag or manual dispatch, and attaches both to the GitHub Release.
```

Insert two steps — `Ensure Inno Setup` **before** the `Package` step, and `Smoke-test installer` **after** it:

```yaml
      - name: Ensure Inno Setup
        shell: pwsh
        # ISCC is preinstalled on windows-latest; this guard only fires if the image drops it.
        run: |
          if (-not (Get-Command iscc.exe -ErrorAction SilentlyContinue) -and
              -not (Test-Path "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe")) {
            choco install innosetup --no-progress -y
          }

      - name: Smoke-test installer
        shell: pwsh
        run: ./tools/test-windows-installer.ps1 -SetupExe artifacts/win-x64/Boxwright-Setup-${{ steps.version.outputs.value }}-win-x64.exe
```

Update the upload/release globs (both are non-recursive, so `publish/` contents stay excluded):

```yaml
      - name: Upload build artifact
        uses: actions/upload-artifact@v4
        with:
          name: Boxwright-win-x64
          path: |
            artifacts/win-x64/*.zip
            artifacts/win-x64/*.exe

      - name: Attach to release
        if: startsWith(github.ref, 'refs/tags/')
        uses: softprops/action-gh-release@v2
        with:
          files: |
            artifacts/win-x64/*.zip
            artifacts/win-x64/*.exe
```

- [ ] **Step 2: Sanity-check the YAML**

Run: `pwsh -c "Install-Module powershell-yaml -Scope CurrentUser -Force; ConvertFrom-Yaml (Get-Content .github/workflows/package-windows.yml -Raw) | Out-Null; 'YAML OK'"`
Expected: `YAML OK`. (If module install is undesirable, a careful visual diff review is the fallback; the workflow gets a real run in Step 4.)

- [ ] **Step 3: Commit**

```powershell
git add .github/workflows/package-windows.yml
git commit -m "ci: build, smoke-test and release the Windows installer alongside the ZIP"
```

- [ ] **Step 4: After the branch is pushed/merged, trigger a `workflow_dispatch` run**

Run: `gh workflow run package-windows.yml -f version=0.0.0-ci-test` (on the branch where the changes exist; use `--ref <branch>` if not on the default branch).
Expected: the run goes green, and the `Boxwright-win-x64` artifact contains both the ZIP and the setup EXE. This is the real verification of Step 1; do not skip it.

---

### Task 4: ADR-0022 + ADR index

**Files:**
- Create: `docs/adr/0022-windows-installer-inno-setup.md`
- Modify: `docs/adr/README.md` (index table currently ends at ADR-0014 — it is stale; rows 0015–0021 are missing and get added here too)

**Interfaces:**
- Consumes: nothing from other tasks (docs only).
- Produces: ADR-0022, referenced by Task 5's checklist/backlog edits.

- [ ] **Step 1: Write the ADR**

Create `docs/adr/0022-windows-installer-inno-setup.md`:

```markdown
# ADR-0022: Windows installer via Inno Setup (per-user, alongside the ZIP)

- **Status:** Accepted
- **Date:** 2026-07-21

## Context
ADR-0009 shipped Windows as a self-contained ZIP and explicitly deferred "a real
MSI/installer (Start-menu, uninstall)" as a later refinement. That refinement is now due:
a stranger expects an installer with a Start-menu entry, in-place upgrades, and a clean
uninstall. Constraints from the design spec
(`docs/superpowers/specs/2026-07-20-windows-installer-design.md`): GitHub Releases only,
per-user with no UAC (no-admin directive), in-place upgrades, still unsigned.

## Decision
- **Inno Setup 6** compiles `packaging/windows-installer.iss` over the **same staged
  `publish/` tree** the ZIP is built from, producing
  `Boxwright-Setup-<version>-win-x64.exe`. The ZIP keeps shipping as the portable option.
- **Per-user, no UAC:** `PrivilegesRequired=lowest` → `%LOCALAPPDATA%\Programs\Boxwright`.
  The tree is identical to the unzipped ZIP (`qemu/` beside the exe), so `QemuLocator`
  needs no changes.
- **In-place upgrades** via a permanent `AppId` GUID; `[InstallDelete]` clears the old
  `qemu/` subtree first so files from an older weilnetz pin cannot linger.
- **Never auto-kill QEMU:** Restart Manager (`CloseApplications=yes`) prompts the user to
  close Boxwright / running VMs; `RestartApplications=no`. Killing a VM risks guest
  corruption.
- **Uninstall preserves VM data:** removes only the install dir + shortcuts;
  `%LOCALAPPDATA%\Boxwright` (VMs, logs, settings) is untouched and the uninstaller says so.
- **CI:** the existing `package-windows.yml` job compiles the installer (ISCC is
  preinstalled on `windows-latest`, choco fallback), runs a silent install/uninstall smoke
  test (`tools/test-windows-installer.ps1`), and attaches both artifacts to the Release.

## Consequences
- **Easier:** one-click install, Start-menu entry, Apps-list uninstall, safe upgrades;
  winget submission becomes possible later (winget accepts Inno EXEs) once signing lands.
- **Harder / accepted:** still unsigned → SmartScreen friction on the setup EXE too;
  ~300 MB per user for per-user installs; Inno Setup is a build-time tool dependency.

## Alternatives considered
- **WiX (MSI):** true MSI, but per-user MSI is awkward (ICE warnings, folder redirection),
  and harvesting thousands of QEMU files needs generated component lists. Heavy for a
  GitHub-Releases-only, per-user install.
- **MSIX:** effectively requires a trusted code-signing certificate to install — we
  deliberately have no signing yet (ADR-0009). Revisit alongside a signing/Store effort.
- **NSIS:** free and scriptable, but dated tooling and worse per-user/upgrade ergonomics
  than Inno Setup.
```

- [ ] **Step 2: Update the ADR index**

In `docs/adr/README.md`, append to the index table (after the 0014 row — the table is stale, so this also back-fills 0015–0021; each title below matches the H1 of its file):

```markdown
| [0015](0015-windows-unattended-autounattend.md) | Unattended Windows install via an Autounattend.xml ISO seed | Accepted |
| [0016](0016-debian-preseed-unattended.md) | Debian unattended install via an initrd-injected preseed (+ a per-family installer seam) | Accepted |
| [0017](0017-fedora-kickstart-unattended.md) | Fedora unattended install via an Anaconda kickstart (netinst) | Accepted |
| [0018](0018-virtio-win-performance-path.md) | virtio-win performance path for unattended Windows installs | Accepted |
| [0019](0019-live-vm-performance-metrics.md) | Live VM performance metrics (CPU / RAM / disk sparklines) | Accepted |
| [0020](0020-remote-os-catalog.md) | Remote / community OS catalog (remote → cache → bundled) | Accepted |
| [0021](0021-external-live-snapshots.md) | External / live VM snapshots (snapshot a running VM) | Accepted |
| [0022](0022-windows-installer-inno-setup.md) | Windows installer via Inno Setup (per-user, alongside the ZIP) | Accepted |
```

Before committing, confirm each back-filled row's Status against the `- **Status:**` line
in its file (`Select-String -Path docs\adr\00*.md -Pattern 'Status'`); adjust any that are
not "Accepted".

- [ ] **Step 3: Commit**

```powershell
git add docs/adr/0022-windows-installer-inno-setup.md docs/adr/README.md
git commit -m "docs: ADR-0022 Windows installer via Inno Setup; back-fill ADR index"
```

---

### Task 5: Release checklist, READMEs, roadmap, backlog

**Files:**
- Modify: `docs/release-checklist.md` (insert a new section after "## Artifact", i.e. after the README performance line)
- Modify: `packaging/README-FIRST.txt` (add intro paragraph after the title)
- Modify: `README.md:69-78` (Windows download steps)
- Modify: `docs/roadmap.md:63-65` (installable-artifact bullet)
- Modify: `docs/backlog.md:659-671` (PKG-2 entry)

**Interfaces:**
- Consumes: ADR-0022 (Task 4), artifact name `Boxwright-Setup-<version>-win-x64.exe`, smoke-test script name (Task 1).
- Produces: docs only; nothing downstream.

- [ ] **Step 1: Release checklist — add an installer section**

In `docs/release-checklist.md`, insert after the last bullet of the `## Artifact` section (the "README performance copy" line) and before `## Linux (AppImage)`:

```markdown
## Installer (Windows setup EXE, ADR-0022)
- [ ] `Boxwright-Setup-<version>-win-x64.exe` installs on a clean Windows machine with
      **no admin / no UAC prompt**, into `%LOCALAPPDATA%\Programs\Boxwright`.
- [ ] Running the new setup over an existing install upgrades in place: old `qemu\` tree
      replaced (no stale files from the previous pin), shortcuts intact.
- [ ] Uninstall from the Apps list removes the install dir + shortcuts and leaves
      `%LOCALAPPDATA%\Boxwright` (VMs, logs, settings) in place.
- [ ] `THIRD-PARTY-NOTICES.txt` is present in the installed tree (same GPL obligations as
      the ZIP — the installer ships the identical staged tree).
- [ ] The CI smoke test (`tools/test-windows-installer.ps1`) passed for the released EXE.
```

- [ ] **Step 2: README-FIRST.txt — document both artifacts**

Insert after the `=====` title underline (line 2), before section "1)":

```text

Two ways to get Boxwright (both on the GitHub Releases page):
  - Boxwright-Setup-<version>-win-x64.exe  - recommended: per-user installer (no admin
    prompt), Start-menu entry, in-place upgrades, uninstall from the Apps list.
  - Boxwright-<version>-win-x64.zip        - portable: extract anywhere, run Boxwright.App.exe.
Uninstalling or deleting the app never touches your VMs (see 5).
```

- [ ] **Step 3: README.md — update the Windows install steps**

Replace `README.md` lines 71–74 (the current steps 1–2):

```markdown
1. Download the latest `Boxwright-<version>-win-x64.zip` from the
   [Releases](https://github.com/eko1495/Boxwright/releases) page.
2. Extract it anywhere and run `Boxwright.App.exe`. It is **self-contained** (no .NET
   install needed) and **QEMU is bundled** (no QEMU install needed).
```

with:

```markdown
1. Download either artifact from the
   [Releases](https://github.com/eko1495/Boxwright/releases) page:
   - **Recommended:** `Boxwright-Setup-<version>-win-x64.exe` — per-user installer (no
     admin prompt), Start-menu entry, in-place upgrades, uninstall from the Apps list.
   - **Portable:** `Boxwright-<version>-win-x64.zip` — extract anywhere and run
     `Boxwright.App.exe`.
2. Either way it is **self-contained** (no .NET install needed) and **QEMU is bundled**
   (no QEMU install needed).
```

(Keep existing steps 3–4 — SmartScreen and virt-viewer — unchanged.)

- [ ] **Step 4: Roadmap — record the installer**

In `docs/roadmap.md`, replace lines 63–65:

```markdown
- [ ] Installable artifact per OS (MSI/zip, AppImage/Flatpak, signed `.app`).
      (Windows `package-windows.ps1`/`.yml` + Linux AppImage `package-linux.sh`/`.yml`
      done; **macOS `.app` pending**.)
```

with:

```markdown
- [ ] Installable artifact per OS (MSI/zip, AppImage/Flatpak, signed `.app`).
      (Windows ZIP + Inno Setup installer (ADR-0022) via `package-windows.ps1`/`.yml`;
      Linux AppImage `package-linux.sh`/`.yml`; **macOS `.app` pending**.)
```

- [ ] **Step 5: Backlog — resolve PKG-2**

In `docs/backlog.md`, check all three PKG-2 acceptance-criteria boxes (`- [ ]` → `- [x]`, lines 668–671) and append after the last criterion:

```markdown
- **✅ Resolved (ADR-0022):** Windows ships the ZIP **and** an Inno Setup per-user
  installer (`Boxwright-Setup-<version>-win-x64.exe`: no UAC, in-place upgrades, uninstall
  preserves VM data), built by `tools/package-windows.ps1` + `package-windows.yml` with a
  CI silent install/uninstall smoke test (`tools/test-windows-installer.ps1`). The
  clean-machine run remains part of GATE-1.
```

- [ ] **Step 6: Verify and commit**

Verify: re-read each edited section; confirm no checklist item promises anything the implementation doesn't do, and performance copy is unchanged (Directive 9).

```powershell
git add docs/release-checklist.md packaging/README-FIRST.txt README.md docs/roadmap.md docs/backlog.md
git commit -m "docs: installer in release checklist, READMEs, roadmap; resolve PKG-2 (ADR-0022)"
```

---

## Final verification (whole plan)

- [ ] `git log --oneline` shows the five commits above.
- [ ] Fresh end-to-end: `pwsh tools\package-windows.ps1 -Version 0.0.0-final -QemuDir (Resolve-Path artifacts\installer-test\fake-qemu)` then `pwsh tools\test-windows-installer.ps1 -SetupExe artifacts\win-x64\Boxwright-Setup-0.0.0-final-win-x64.exe` → exit 0.
- [ ] `dotnet build` still clean (nothing should have touched C#, but confirm no stray edits).
- [ ] Clean up local test artifacts: `Remove-Item -Recurse -Force artifacts\installer-test` (the `artifacts/` tree is untracked build output).
- [ ] The manual clean-VM pass (install → create VM → boot → upgrade → uninstall) is a **release-time** checklist item, not part of this branch.

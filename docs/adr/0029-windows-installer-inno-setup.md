# ADR-0029: Windows installer via Inno Setup (per-user, alongside the ZIP)

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
- **Never auto-kill QEMU (interactive installs):** Restart Manager (`CloseApplications=yes`)
  prompts the user to close Boxwright / running VMs; `RestartApplications=no`. Killing a VM
  risks guest corruption. **Caveat:** silent installs (`/SILENT`, `/VERYSILENT`) follow
  Inno's auto-close semantics — no prompt — so do not upgrade silently while a VM is
  running; a `[Code]` guard for silent mode is a prerequisite for any future winget
  submission (winget installs silently).
- **Uninstall preserves VM data:** removes only the install dir + shortcuts;
  `%LOCALAPPDATA%\Boxwright` (VMs, logs, settings) is untouched and the uninstaller says so.
- **CI:** the existing `package-windows.yml` job compiles the installer (ISCC is
  preinstalled on `windows-latest`, choco fallback), runs a silent install/uninstall smoke
  test (`tools/test-windows-installer.ps1`), and attaches both artifacts to the Release.

## Consequences
- **Easier:** one-click install, Start-menu entry, Apps-list uninstall, safe upgrades;
  winget submission becomes possible later (winget accepts Inno EXEs) once signing lands.
- **Harder / accepted:** still unsigned → SmartScreen friction on the setup EXE too;
  ~300 MB per user for per-user installs; Inno Setup (6.3+) is a build-time tool dependency.

## Alternatives considered
- **WiX (MSI):** true MSI, but per-user MSI is awkward (ICE warnings, folder redirection),
  and harvesting thousands of QEMU files needs generated component lists. Heavy for a
  GitHub-Releases-only, per-user install.
- **MSIX:** effectively requires a trusted code-signing certificate to install — we
  deliberately have no signing yet (ADR-0009). Revisit alongside a signing/Store effort.
- **NSIS:** free and scriptable, but dated tooling and worse per-user/upgrade ergonomics
  than Inno Setup.

# Windows installer (Inno Setup) — design

- **Date:** 2026-07-20
- **Status:** Approved (brainstorming session)
- **Refines:** ADR-0009 (self-contained ZIP). The ZIP stays; this adds the deferred
  "real installer" on top of the same staged tree.

## Goal

A stranger downloads `Boxwright-Setup-<version>-win-x64.exe` from GitHub Releases,
runs it with **no admin prompt**, gets a Start-menu entry, and can later upgrade
in place or uninstall cleanly — with VM data always preserved.

## Decisions (from the brainstorm)

| Question | Decision |
| --- | --- |
| Distribution | GitHub Releases only (winget/Store later, once signing lands) |
| Install scope | Per-user, no UAC (`%LocalAppData%\Programs\Boxwright`) |
| Upgrades | In-place via stable Inno `AppId`; no in-app update checker |
| Technology | Inno Setup 6 (WiX rejected: per-user MSI awkward, heavy tooling; MSIX rejected: requires signing we don't have) |

## Artifact & layout

- `packaging/windows-installer.iss`, compiled with ISCC over the **same staged
  `publish/` tree** `tools/package-windows.ps1` already builds for the ZIP.
  One staging pass produces both artifacts; the ZIP remains the portable option.
- `PrivilegesRequired=lowest` → installs to `%LocalAppData%\Programs\Boxwright`.
  The full tree lands there, `qemu/` beside the exe, so `QemuLocator` works
  exactly as with the unzipped ZIP. **Zero app-code changes.**
- Start-menu shortcut always; desktop icon as an opt-in task. No PATH edits, no
  file associations, no services (no-daemon directive).
- The `.iss` globs the staged tree (no per-file listing) — QEMU pin bumps need
  no installer edits. Version and tree path arrive via `/D` defines.

## Install / upgrade / uninstall behavior

- **Upgrade:** fixed `AppId` GUID → newer setup detects and upgrades in place.
  `[InstallDelete]` clears the `qemu/` subtree first so files from an older
  weilnetz pin cannot linger; app files are overwritten normally. Downgrades are
  not blocked (running an older setup installs the older version).
- **Running-app safety:** `CloseApplications=yes` (Restart Manager) detects
  Boxwright or spawned `qemu-system-*.exe` holding files under the install dir
  and prompts the user to close them. **Never auto-kill QEMU** — killing a
  running VM risks guest corruption. Setup proceeds only when files are free.
- **Uninstall:** Apps-list entry; removes install dir + shortcuts and nothing
  else. VM configs, disks, and settings survive; the uninstaller shows a note
  that VM data was left in place and where.
- **SmartScreen:** still unsigned (signing separately deferred, ADR-0009). Same
  "More info → Run anyway" guidance in README-FIRST and release notes.

## Build & CI integration

- `tools/package-windows.ps1`: new final step (plus `-SkipInstaller` switch)
  runs `ISCC.exe packaging\windows-installer.iss` after staging + zipping.
  Local runs need Inno Setup 6 installed (registry/well-known-path lookup,
  clear error otherwise).
- `.github/workflows/package-windows.yml`: same job now attaches **both** the
  ZIP and the setup EXE to the release. ISCC is preinstalled on
  `windows-latest`; fallback is a one-line choco install (workflow comment).

## Docs

- New **ADR-0022 — Windows installer via Inno Setup** (refines ADR-0009, which
  stays Accepted): per-user/no-UAC, AppId upgrades, no-auto-kill policy,
  WiX/MSIX rejection rationale.
- `docs/release-checklist.md`: installer section — clean-machine install with
  no admin, upgrade-over-existing, uninstall preserves VM data,
  THIRD-PARTY-NOTICES present in the installed tree.
- `packaging/README-FIRST.txt` + README: document both artifacts (installer =
  recommended, ZIP = portable).
- Roadmap/backlog: check off PKG-2 with a pointer to ADR-0022.

## Testing

- Installer logic is not unit-testable; the release checklist covers the manual
  clean-VM pass (install → create VM → boot → upgrade → uninstall).
- Automated guard in CI after ISCC: silent install
  (`/VERYSILENT /CURRENTUSER`) on the runner, assert `Boxwright.App.exe` and
  `qemu\qemu-system-x86_64.exe` exist at the per-user path, silent uninstall.
  Catches broken `.iss` globs without a human.

## Out of scope

- Code signing (separate future effort; unblocks winget/MSIX/Store).
- In-app update checker.
- Per-machine or dual-scope installs.
- Bundling virt-viewer (CORE-10 stays open).

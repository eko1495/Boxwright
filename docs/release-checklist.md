# Release checklist

Gate before publishing a release artifact (PKG-5). The GPL section enforces ADR-0005 / ADR-0007 for the
QEMU **bundled in the Windows ZIP**; the Linux AppImage bundles no QEMU (system QEMU, ADR-0011).

## GPL / bundled QEMU
- [ ] Bundled QEMU is the pinned weilnetz build, **unmodified** (byte-for-byte from the installer).
- [ ] `packaging/THIRD-PARTY-NOTICES.txt` is present in the ZIP and states the exact build file,
      SHA-512, upstream source link, and the written source offer.
- [ ] The QEMU pin (file + SHA-512) in `tools/package-windows.ps1` matches the NOTICES and ADR-0009.
- [ ] No QEMU linking / P/Invoke / vendored source anywhere in Boxwright (Directive 2 / ADR-0005).

## Artifact
- [ ] `dotnet test` green; `dotnet build -c Release` clean (warnings-as-errors).
- [ ] The ZIP unzips and runs on a clean Windows machine with **no** QEMU on PATH and **no**
      installed .NET (self-contained bundles the runtime).
- [ ] A VM created from the unzipped app uses the **bundled** QEMU: the per-VM `qemu.log` launch
      header `Command:` line points under `...\qemu\qemu-system-x86_64.exe`.
- [ ] `README-FIRST.txt` is present (SmartScreen + virt-viewer guidance).
- [ ] README performance copy stays honest (Directive 9).

## Installer (Windows setup EXE, ADR-0022)
- [ ] `Boxwright-Setup-<version>-win-x64.exe` installs on a clean Windows machine with
      **no admin / no UAC prompt**, into `%LOCALAPPDATA%\Programs\Boxwright`.
- [ ] Running the new setup over an existing install upgrades in place: old `qemu\` tree
      replaced (no stale files from the previous pin), shortcuts intact.
- [ ] Upgrading interactively while Boxwright or a VM is running shows the close-applications
      prompt and does not kill QEMU (do not test this with a VM you care about).
- [ ] Uninstall from the Apps list removes the install dir + shortcuts and leaves
      `%LOCALAPPDATA%\Boxwright` (VMs, logs, settings) in place.
- [ ] `THIRD-PARTY-NOTICES.txt` is present in the installed tree (same GPL obligations as
      the ZIP — the installer ships the identical staged tree).
- [ ] The CI smoke test (`tools/test-windows-installer.ps1`) passed for the released EXE.

## Linux (AppImage)
- [ ] No bundled QEMU (system QEMU on Linux, ADR-0011) — so no GPL source-offer obligation;
      `package-linux.sh` bundles only the app + .NET runtime.
- [ ] The `appimagetool` pin (URL + SHA-256) in `tools/package-linux.sh` is verified at build time.
- [ ] The `.AppImage` runs on a clean Linux desktop with system `qemu-system-x86` + `virt-viewer`:
      create a VM, boot an ISO, reach the installer; the display opens via remote-viewer.
- [ ] `README-FIRST-linux.txt` is present (qemu / virt-viewer / system-lib guidance).

## Versioning / release
- [ ] Tag is `vMAJOR.MINOR.PATCH`; the `package-windows` and `package-linux` workflows produced and
      attached the ZIP + AppImage to the Release.

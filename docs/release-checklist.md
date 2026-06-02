# Release checklist

Gate before publishing a Windows artifact (PKG-5; enforces the GPL hygiene of ADR-0005 / ADR-0007).

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

## Versioning / release
- [ ] Tag is `vMAJOR.MINOR.PATCH`; the `package-windows` workflow produced and attached the ZIP.

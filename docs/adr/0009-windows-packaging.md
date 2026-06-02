# ADR-0009: Windows packaging — self-contained ZIP with bundled QEMU

- **Status:** Accepted
- **Date:** 2026-06-02

## Context
The MVP exit gate (GATE-1) needs a Windows artifact a stranger can install on a clean
machine — no separately-installed QEMU, no .NET install — and boot a guest in under 10
minutes. ADR-0007 set the direction ("bundle QEMU per platform") but left the artifact
format, the QEMU source, the firmware layout, and the viewer question to PKG-1/PKG-2.
This resolves them.

## Decision
- **Artifact: a self-contained ZIP** (`Boxwright-<version>-win-x64.zip`) for the first cut.
  `dotnet publish -r win-x64 --self-contained` bundles the .NET 10 runtime; the ZIP is
  unzip-and-run. **Not trimmed** (Avalonia loads XAML/converters by reflection — the IL
  trimmer breaks it) and **not single-file** (avoids native-asset temp extraction and keeps a
  predictable folder so `qemu/` sits beside the exe). A real MSI/installer (Start-menu,
  uninstall) is a later refinement, not a gate blocker.
- **Bundle QEMU from the weilnetz w64 build**, fetched at build time (pinned version + SHA-256),
  silent-extracted, and copied **unmodified** into `<exedir>/qemu/`. Its `share/edk2-*` firmware
  comes along, so UEFI (pflash) works with no extra bundling.
- **The app finds bundled QEMU at `<exedir>/qemu/`** — `ServiceConfiguration` passes
  `Path.Combine(AppContext.BaseDirectory, "qemu")` to `QemuLocator`. It is inert when the folder
  is absent (the locator falls through to PATH), so the same build serves dev and packaged.
- **Do not bundle `remote-viewer`** — reaffirms ADR-0008. The user installs virt-viewer once
  (documented). The eventual embedded VNC viewer removes this step.
- **GPL:** QEMU ships unmodified with a written source offer + upstream link (ADR-0005); a release
  checklist (`docs/release-checklist.md`) gates it. Boxwright's own code stays MIT.
- **No code signing yet** — the unsigned exe triggers SmartScreen; documented as "More info → Run
  anyway." Signing is a later step.

## Consequences
- **Easier:** a stranger unzips and runs; QEMU + firmware are bundled; the build is reproducible
  in CI and uploaded as a release asset. The same binary works in dev (PATH fallback).
- **Harder / accepted:** ~200–350 MB ZIP (self-contained .NET + the whole weilnetz tree —
  acceptable per ADR-0007); the user still installs virt-viewer for the display; SmartScreen
  friction until signing; the weilnetz pin needs an occasional bump.

## Alternatives considered
- **MSI/installer now:** rejected for the first cut — more tooling, not needed to clear the gate;
  planned as a follow-up.
- **Framework-dependent (require a .NET install):** rejected — breaks the clean-machine gate.
- **Single-file / trimmed:** rejected — Avalonia reflection breaks under trimming; single-file adds
  extraction + base-directory quirks for no benefit here.
- **Bundle virt-viewer:** rejected (ADR-0008) — a Red Hat/GTK payload; an optional install stays lean.

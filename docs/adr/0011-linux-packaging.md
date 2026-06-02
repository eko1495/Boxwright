# ADR-0011: Linux packaging — AppImage with system QEMU

- **Status:** Accepted
- **Date:** 2026-06-02

## Context
ADR-0009 produced a Windows artifact; cross-platform parity (Directive 4) needs a Linux one
(backlog PKG-3), and the cross-platform CI now proves the code builds + passes all tests on Linux.
Two questions were open: the artifact **format** (AppImage vs Flatpak) and whether to **bundle QEMU**
(as the Windows ZIP does) or rely on the system's.

## Decision
- **AppImage, not Flatpak.** Boxwright spawns `qemu-system-*` child processes, needs `/dev/kvm`, and
  shells out to `remote-viewer`. Flatpak's sandbox fights all three — it would need broad
  host-filesystem + device holes (defeating the sandbox) plus `flatpak-spawn` gymnastics for child
  processes. AppImage is an unprivileged, download-and-run single file that matches the Windows-ZIP
  model and the "control QEMU as a child process, no daemon" directives (1 & 6).
- **Do NOT bundle QEMU.** On Linux, `qemu-system-*` / `qemu-img` and `virt-viewer` are ubiquitous
  system packages; `QemuLocator` already resolves them from PATH and a missing tool degrades
  gracefully with a clear in-app message (Directive 4). This keeps the AppImage small, avoids
  fragile cross-distro bundling of QEMU's many shared-library dependencies, and — since we do not
  redistribute QEMU — carries **no GPL source-offer obligation** (architecture §10 explicitly allows
  system QEMU on Linux). The user installs qemu + virt-viewer once (`packaging/README-FIRST-linux.txt`).
- **Self-contained .NET, not trimmed/single-file** — same as ADR-0009 (Avalonia loads XAML/converters
  by reflection; the trimmer breaks it).
- **Build in CI** (`package-linux.yml`, ubuntu-latest) with a **pinned appimagetool (1.9.1 + SHA-256)**,
  run **FUSE-free** via `APPIMAGE_EXTRACT_AND_RUN=1` (GitHub-hosted runners lack FUSE). The `AppRun`
  must **not** override `$HOME` — Boxwright's data lives under `~/.local/share/Boxwright`.
- **No code signing yet** — AppImages are unsigned by default; revisit alongside the Windows signing story.

## Consequences
- **Easier:** Linux users download one file, `chmod +x`, run it; the AppImage stays small (no QEMU);
  the same source builds Windows + Linux from CI; no GPL bundling burden on Linux.
- **Harder / accepted:** the user installs qemu + virt-viewer themselves (idiomatic on Linux, but not
  the zero-install of the Windows ZIP); the AppImage can't be built or run on the Windows dev box
  (CI + a Linux box are the tests); a few common X11/font system libs are assumed present.

## Alternatives considered
- **Flatpak:** rejected — its sandbox fights `/dev/kvm`, child-process spawning, and `remote-viewer`;
  high permission complexity for an app designed to drive host processes.
- **Bundle QEMU in the AppImage:** rejected for the first cut — large, fragile cross-distro library
  bundling, plus the GPL source-offer obligation, for little gain when system QEMU is one package
  away. A possible future option (parity with the Windows zero-install experience).
- **Distro packages (.deb/.rpm) or a multi-format tool (PupNet):** deferred — more per-distro
  maintenance than a single portable AppImage for a first Linux cut.

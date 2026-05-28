# ADR-0002: Use Avalonia UI for the cross-platform GUI

- **Status:** Accepted
- **Date:** 2026-05-28

## Context
The project must ship a single GUI codebase on Windows, macOS, and Linux, and
the maintainer's primary expertise is C#/.NET. We evaluated Avalonia, .NET MAUI,
Qt, Electron/Tauri, GTK, and Flutter.

Key constraints: genuine Linux desktop support (the maintainer develops on
Fedora/KDE), leverage of existing C#/.NET skills, native-ish feel, and a healthy
ecosystem.

## Decision
Use **Avalonia UI** (11.x/12.x) with **MVVM** (CommunityToolkit.Mvvm).

Rationale:
- **Real Linux desktop support** — unlike .NET MAUI, whose desktop targets are
  Windows (WinUI) and macOS (Catalyst) with **no first-class Linux**. That alone
  disqualifies MAUI for this project.
- **Leverages C#/.NET** directly; QMP/JSON is trivial with `System.Text.Json` +
  sockets.
- **Production-grade and active** — Avalonia is widely used in real desktop
  software and is actively developed, with a large NuGet footprint.
- **Single XAML/MVVM codebase** across all three OSes, AOT-publishable.

## Consequences
- **Easier:** one UI codebase, all three desktops; familiar XAML/MVVM; the
  maintainer is productive immediately.
- **Easier:** the non-UI work (QMP client) is plain .NET and reusable.
- **Harder:** Avalonia has slightly less tooling than WPF (no Blend-style visual
  designer). Acceptable for a desktop VM manager.
- **Harder:** no Avalonia-native SPICE/VNC widget exists, so the *embedded*
  display (later milestones) needs custom work. The MVP sidesteps this by
  launching `remote-viewer` (see ADR-0004).

## Alternatives considered
- **.NET MAUI:** rejected — no first-class Linux desktop target; dealbreaker.
- **Qt (C++/PyQt):** what every dead QEMU GUI used; mature SPICE/GTK integration
  on Linux, but the maintainer has no Qt/C++ background — high ramp-up.
- **Electron:** heavy (~100 MB baseline); web stack, not C#.
- **Tauri:** small and modern but requires Rust + web frontend; abandons the C#
  advantage.
- **GTK (C#/gtk-rs):** looks alien on Windows/macOS — the very reason GNOME
  Boxes never went cross-platform.
- **Flutter:** Quickgui proves it can work, but its desktop story is
  second-class and Quickgui itself stalled.

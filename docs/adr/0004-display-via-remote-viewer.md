# ADR-0004: MVP display via external remote-viewer (SPICE)

- **Status:** Accepted
- **Date:** 2026-05-28

## Context
Showing the VM screen is the hardest UX problem in a QEMU GUI. Options: embed
QEMU's built-in SDL/GTK window, embed a VNC client, embed a SPICE client, or
launch an external viewer. There is no Avalonia-native SPICE/VNC widget, and a
good embedded SPICE client is a multi-month effort (FFI to spice-gtk is painful
cross-platform; a pure implementation is large).

We want a working display in the MVP without sinking the whole schedule into it.

## Decision
For the **MVP**, QEMU runs a **SPICE server** (`-spice`) and Boxwright launches
the external **`remote-viewer`** (from the virt-viewer package) against it. No
embedding work; works on all three OSes today.

Display evolves in later milestones:
- **v0.3:** embedded **VNC** client inside an Avalonia control (pure-C# feasible)
  to drop the external dependency.
- **v1.0:** embedded **SPICE** client (clipboard, folder sharing, multi-monitor,
  USB redirect).

We explicitly **do not** embed QEMU's own SDL/GTK window in Avalonia.

## Consequences
- **Easier:** the MVP ships with a real, full-featured display almost for free;
  SPICE already gives clipboard/USB sharing via remote-viewer.
- **Harder / accepted:** the VM screen is a separate window in the MVP, and
  users must have virt-viewer available (bundled or documented per OS).
- **Future cost:** embedded display is deferred, not avoided — tracked in the
  roadmap as v0.3/v1.0.

## Alternatives considered
- **Embed QEMU SDL/GTK window:** rejected — fragile, platform-specific.
- **Embedded SPICE first:** rejected for MVP — too large to block the first
  release on.
- **VNC-only:** viable but loses SPICE's clipboard/folder/USB sharing; we prefer
  SPICE for the MVP display and add embedded VNC later as a dependency-reducer.

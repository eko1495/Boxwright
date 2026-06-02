# ADR-0012: Embedded VNC display

- **Status:** Accepted
- **Date:** 2026-06-02

## Context
ADR-0004 shipped the MVP display via the external **remote-viewer** and named an embedded **VNC**
client as the "v0.3" step; ADR-0008 reaffirmed VNC (neutral RFB, embeddable in pure C#/Avalonia) as
the way off virt-viewer. With the MVP shipped (v0.1–v0.3), this implements that embedded display.
QEMU already exposes `-vnc` (`CommandLineBuilder`), and Directive 7 forbids embedding QEMU's own
**SDL/GTK window** — not an embedded *client* rendering into our own Avalonia control.

## Decision
- **Render the guest in-app with the MarcusW.VncClient.Avalonia library** — the community fork
  `Community.MarcusW.VncClient(.Avalonia)` **2.0.4** (MIT, net10.0, Avalonia ≥ 11.3.11). It ships an
  Avalonia `VncView` that handles RFB encodings + keyboard/mouse + threading. **App-layer dependency
  only** — `Boxwright.Core`/`Boxwright.Qmp` stay dependency-free (Directive 8). The community fork is
  used because the original `MarcusW.VncClient.Avalonia` is stuck at `1.0.0-alpha4` (old Avalonia).
- **Opt-in by protocol:** "Open display" opens the embedded `VncDisplayWindow` when the running VM's
  `Display.Protocol == "vnc"`; SPICE (the default) still opens remote-viewer. No change to existing
  SPICE VMs — the user selects VNC in Settings.
- **A dedicated window** (`VncDisplayWindow`), not an in-main-window pane — a display surface needs
  space + keyboard focus.
- **Auth: none** — QEMU's `-vnc` has no password (RFB security "None"); the client uses a
  no-credential handler that fails clearly if a guest ever demands auth.
- **MVP scope:** connect + render + keyboard/mouse + clean connect/disconnect. **Deferred:** clipboard,
  dynamic/desktop resize, TLS/password auth, an explicit embedded-vs-external toggle, and **embedded
  SPICE** (still the v1.0 target per ADR-0004/0008).

This **extends** ADR-0004/0008 (does not supersede them): the staged display plan stands; SPICE-embed
remains future work.

## Consequences
- **Easier:** VNC guests render inside Boxwright with no external viewer; one MIT App-layer dependency
  does the RFB/encoding/input heavy lifting, so this landed in days rather than weeks.
- **Harder / accepted:** the first third-party *functional* dependency in `Boxwright.App` (beyond the
  Avalonia/FluentAvalonia/MVVM stack); embedded display only for VNC guests (SPICE keeps remote-viewer);
  the rendering path can't be exercised on headless CI — it's validated manually on a box with QEMU.

## Alternatives considered
- **DIY zero-dep `Boxwright.Vnc` RFB client** (parallel to `Boxwright.Qmp`): on-brand with the
  zero-dependency ethos, but ~2–3 weeks plus owning every protocol edge case (keysym tables, encodings,
  threading). The library gets a robust result far sooner; revisitable if the dependency disappoints.
- **The original `MarcusW.VncClient.Avalonia`:** stuck at `1.0.0-alpha4` against an old Avalonia; the
  community fork is stable (2.x) and current for Avalonia 11.
- **In-main-window pane:** rejected — a display surface wants its own resizable window + keyboard focus.
- **Embed SPICE now:** deferred to v1.0 (no Avalonia-native SPICE widget; a much larger effort) per
  ADR-0004/0008.

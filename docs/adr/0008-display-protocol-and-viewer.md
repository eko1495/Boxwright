# ADR-0008: Per-VM display protocol (SPICE/VNC); viewer external until embedded VNC

- **Status:** Accepted
- **Date:** 2026-05-30

## Context
ADR-0004 chose the external **`remote-viewer`** (SPICE) for the MVP display and named
an embedded VNC client as the later dependency-reducer. Using it in anger surfaced two
forces:

1. `remote-viewer` (virt-viewer) is open source, but **external, infrequently updated on
   Windows (11.0, 2021), and built on SPICE-GTK + libvirt-glib (Red Hat)**. It's a
   dependency we'd like to shed eventually — and one that surprised us by dragging in
   libvirt.
2. The Windows QEMU builds don't all ship SPICE, whereas **VNC is always compiled in**.
   We want a fallback that doesn't assume a SPICE-capable QEMU.

There are really **two independent axes**, easily conflated:

| Axis | Options | Drops virt-viewer? |
|------|---------|--------------------|
| **Protocol** (what QEMU serves) | SPICE / VNC | No |
| **Viewer** (what connects) | external `remote-viewer` / *embedded client* | Yes (embedded) |

`remote-viewer` speaks **both** protocols, so choosing VNC does not by itself drop the
virt-viewer dependency — only an embedded viewer does.

## Decision
- **Expose the protocol per VM** (`VmConfig.Display.Protocol` = `spice` | `vnc`), editable
  in the settings panel. Both launch via `remote-viewer` (`spice://` / `vnc://`); SPICE
  stays the default (richer: clipboard/USB). The display launcher honours the protocol the
  VM was launched with.
- **Keep `remote-viewer` as the MVP viewer.** It is open source and not lock-in, and
  Boxwright only spawns it as a subprocess — it is **not** a libvirt backend, so Directive 3
  stands.
- **Do not bundle virt-viewer** (PKG-1): make it an optional, documented user install, so
  the Windows artifact stays lean and free of Red Hat / libvirt payload.
- **The genuine "move off virt-viewer" is an embedded VNC viewer** — reaffirming ADR-0004's
  v0.3 step. VNC (the neutral RFB protocol) is embeddable in pure C#/Avalonia and drops
  `remote-viewer`, SPICE-GTK, **and** libvirt-glib at once. The "external vs embedded"
  viewer setting ships **with** that client, not before.

## Consequences
- **Easier:** users get a SPICE/VNC choice now (and a fallback when a QEMU build lacks
  SPICE); the Windows package carries no Red Hat tooling.
- **Harder / accepted:** until the embedded viewer lands, a display still needs
  `remote-viewer` installed; the external-vs-embedded viewer choice is deferred to that
  milestone.
- This **extends**, not supersedes, ADR-0004 — the MVP-display decision stands.

## Alternatives considered
- **VNC-only, drop SPICE:** rejected — loses SPICE's clipboard/USB, and `remote-viewer`
  does VNC too, so it wouldn't shed the dependency anyway.
- **Bundle virt-viewer:** rejected for MVP — adds a stale, Red Hat / libvirt-linked payload
  to the Windows artifact; an optional install keeps it lean.
- **Add the external/embedded viewer toggle now:** premature — there is no embedded viewer
  yet, so the toggle would have only one real position.

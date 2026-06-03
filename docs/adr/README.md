# Architecture Decision Records

Each ADR captures **one** decision, the context that forced it, and the
consequences. ADRs are append-only: to change a decision, add a new ADR that
**supersedes** the old one (and update the old one's status). Keep them short.

## Format

```markdown
# ADR-NNNN: <title>

- **Status:** Proposed | Accepted | Superseded by ADR-XXXX
- **Date:** YYYY-MM-DD

## Context
What problem/force are we responding to?

## Decision
What we are doing.

## Consequences
What becomes easier and harder as a result.

## Alternatives considered
What we rejected and why.
```

## Index

| ADR | Title | Status |
|-----|-------|--------|
| [0001](0001-direct-qmp-not-libvirt.md) | Control QEMU directly via QMP, not libvirt | Accepted |
| [0002](0002-avalonia-ui.md) | Use Avalonia UI for the cross-platform GUI | Accepted |
| [0003](0003-process-per-vm.md) | One QEMU process per VM; auto-detect accelerator | Accepted |
| [0004](0004-display-via-remote-viewer.md) | MVP display via external remote-viewer (SPICE) | Accepted |
| [0005](0005-gpl-hygiene-subprocess.md) | Keep QEMU at arm's length as a subprocess (GPL hygiene) | Accepted |
| [0006](0006-json-vm-config.md) | One VM = one folder + one JSON config; no daemon | Accepted |
| [0007](0007-bundled-qemu-and-qmp-library.md) | Bundle QEMU per platform; ship QMP as a standalone library | Accepted |
| [0008](0008-display-protocol-and-viewer.md) | Per-VM display protocol (SPICE/VNC); viewer external until embedded VNC | Accepted |
| [0009](0009-windows-packaging.md) | Windows packaging: self-contained ZIP with bundled QEMU | Accepted |
| [0010](0010-os-catalog.md) | One-click OS catalog: bundled JSON + verified ISO download to a shared cache | Accepted |
| [0011](0011-linux-packaging.md) | Linux packaging: AppImage with system QEMU (not Flatpak, not bundled) | Accepted |
| [0012](0012-embedded-vnc-display.md) | Embedded VNC display via MarcusW.VncClient (App-layer dep; opt-in by protocol) | Accepted |
| [0013](0013-unattended-install-cloud-init.md) | Unattended install via a cloud-init NoCloud seed (Ubuntu autoinstall; FAT seed) | Accepted |

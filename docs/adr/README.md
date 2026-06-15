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
| [0014](0014-vm-runtime-state-and-reconnect.md) | Persist VM runtime state; re-adopt running QEMU on restart (no daemon) | Accepted |
| [0015](0015-windows-unattended-autounattend.md) | Windows unattended install via an auto-discovered Autounattend.xml seed CD | Accepted |
| [0016](0016-debian-preseed-unattended.md) | Debian unattended install via initrd-injected preseed | Accepted |
| [0017](0017-fedora-kickstart-unattended.md) | Fedora unattended install via initrd-injected kickstart | Accepted |
| [0018](0018-virtio-win-performance-path.md) | Opt-in virtio-win driver injection for the Windows performance path | Accepted |
| [0019](0019-live-vm-performance-metrics.md) | Live VM performance metrics (CPU/RAM/disk) via the host process + QMP | Accepted |
| [0020](0020-remote-os-catalog.md) | Remote, community-maintainable OS catalog wrapping the bundled list | Accepted |
| [0021](0021-external-live-snapshots.md) | External / live snapshots of a running VM (blockdev-snapshot-sync + transaction) | Accepted |
| [0022](0022-headless-cli.md) | Headless command-line interface (`boxwright`) over Core, sharing on-disk state with the GUI | Accepted |
| [0023](0023-usb-passthrough.md) | Host USB passthrough by vendor:product; Linux sysfs enumeration, capability-gated elsewhere | Accepted |
| [0024](0024-bridged-tap-networking.md) | Bridged / TAP networking on Linux (beyond user-mode NAT), capability-gated | Accepted |
| [0025](0025-vm-templates.md) | VM templates: clone instances from a frozen base (+ per-VM MAC) | Accepted (phase 1) |
| [0026](0026-declarative-os-recipes.md) | Declarative OS recipes for community-contributed OS definitions (data, not code) | Accepted (phase 1 + 2a) |

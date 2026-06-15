# ADR-0024: Bridged / TAP networking (Linux), beyond user-mode NAT

- **Status:** Accepted
- **Date:** 2026-06-15

## Context
The default network is user-mode SLIRP (ADR / architecture §7): zero-config, no admin, but the guest
sits behind NAT — it gets no LAN IP, can't be reached from other hosts, and some protocols (broadcast,
DHCP-from-the-guest, mDNS) don't traverse it. Homelab and "VM as a real machine on my network" use cases
want the guest **bridged onto the host LAN** (its own DHCP lease, reachable like any other box), or
attached to a specific **TAP** device. This is the Stage-3 "bridged/TAP networking on Linux" item.

This is unavoidably **Linux-specific** (Directive 4). QEMU bridging on Linux uses either a setuid
`qemu-bridge-helper` (`-netdev bridge,br=…`) or a pre-created TAP device (`-netdev tap,ifname=…`).
Windows and macOS have entirely different mechanisms (no bridge helper; macOS uses `vmnet`), so this
feature must be **capability-gated and degrade with a clear message**, never silently attempted.

## Decision
- **`NetworkConfig` gains `Mode` ∈ `user` (default) | `bridge` | `tap`, plus `Bridge` (default `br0`) and
  `TapDevice` (default `tap0`).** `CommandLineBuilder` emits per mode:
  - `user` → `-netdev user,id=net0[,hostfwd=…]` (unchanged; port-forwards apply here only).
  - `bridge` → `-netdev bridge,id=net0,br=<Bridge>` — uses the host's `qemu-bridge-helper`.
  - `tap` → `-netdev tap,id=net0,ifname=<TapDevice>,script=no,downscript=no` — a pre-created TAP the
    invoking user can open; Boxwright runs no up/down scripts (no root path).

  The NIC `-device <model>,netdev=net0` is the same across modes. This stays a pure function (golden-tested).
- **Capability gate (Linux-only).** A pure `NetworkValidation.EnsureSupportedOnHost(network, isLinux)`
  throws `VmConfigException` for `bridge`/`tap` when `isLinux` is false; `VmLauncher.StartAsync` calls it
  with `OperatingSystem.IsLinux()` before launching. So a bridged VM created on (or copied to) a non-Linux
  host fails fast with a clear message instead of a baffling QEMU error.
- **Host setup stays the user's responsibility (documented, not automated).** Bridged mode needs an
  existing host bridge (e.g. `br0`) and a setuid `qemu-bridge-helper` whitelisted in
  `/etc/qemu/bridge.conf` (`allow br0`); TAP mode needs the TAP device pre-created and owned by the user.
  Boxwright does **not** create bridges, edit `bridge.conf`, or run as root — that would violate the
  "no admin for the default path / no privileged daemon" directives. We surface the requirement; we don't
  perform it.
- **Surface:** the CLI `boxwright net show|set` command edits a VM's network mode (`set <vm> bridge
  --bridge br0`, `… tap --device tap0`, `… user`). Both shells launch a configured VM identically (the
  mode lives in `vm.json`).

## Consequences
- **Easier:** a guest can join the host LAN with its own IP (homelab, services reachable from other
  machines), or attach to a specific TAP for custom topologies — on Linux, opt-in, no change to the
  zero-config default.
- **Harder / accepted:** Linux-only (gated elsewhere). The host-side prerequisites (bridge exists, helper
  setuid + ACL, or TAP pre-created) are the user's to set up; a misconfiguration surfaces as QEMU's error
  in the per-VM log. We deliberately don't preflight the bridge's existence or the helper's setuid bit in
  this cut (a clear follow-up); the OS gate is the only automated check. Per-VM MAC pinning and multiple
  NICs are out of scope for now.

## Alternatives considered
- **Run an up/down `script=` (root) for TAP.** Rejected: it needs QEMU to invoke a privileged script,
  pulling root into the default launch path. `script=no` with a user-owned TAP (or the bridge helper)
  keeps privilege out of Boxwright.
- **Auto-create the bridge / edit `bridge.conf`.** Rejected: that's host network reconfiguration requiring
  root, exactly what the directives forbid. Document the one-time setup instead.
- **vmnet on macOS / a Windows bridge.** Deferred: different mechanisms per OS; out of scope for the
  Linux-first Stage-3 item. The capability gate leaves room to add them behind the same `Mode`.

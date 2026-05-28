# ADR-0001: Control QEMU directly via QMP, not libvirt

- **Status:** Accepted
- **Date:** 2026-05-28

## Context
We need to control QEMU programmatically across Windows, macOS, and Linux. The
two established approaches are: (a) build on **libvirt** (the management daemon
that virt-manager and GNOME Boxes use), or (b) drive **QEMU directly** via its
command line and the QEMU Machine Protocol (QMP).

libvirt is fundamentally a Linux daemon. There is no first-class libvirtd on
Windows; on macOS it is awkward (Homebrew, non-native). Building on libvirt
would force Boxwright to be Linux-only, or to ship/run a libvirtd VM on the
other platforms — which defeats the entire purpose of a cross-platform tool.
This is exactly why virt-manager has never gone cross-platform.

## Decision
Boxwright controls QEMU **directly**: it generates `qemu-system-<arch>` command
lines, launches them as child processes, and manages them at runtime over
**QMP** (JSON over a TCP/Unix socket). Disk operations use the `qemu-img`
subprocess. **No libvirt dependency is introduced, on any platform.**

This is the same approach UTM, Quickemu, and AQEMU all take, and it is the only
one that works uniformly on all three desktop OSes.

## Consequences
- **Easier:** true cross-platform parity; no daemon to install or manage; simple
  packaging; full control over the exact QEMU invocation.
- **Easier:** the resulting QMP client has standalone value (no good C# QMP
  library exists on NuGet today).
- **Harder:** we reimplement conveniences libvirt would have given us (storage
  pools, network definitions, domain XML). We accept this; our scope is a
  desktop VM manager, not a virtualization-management platform.
- **Harder:** we own the command-line-generation correctness surface
  (`CommandLineBuilder`), so it must be well tested.

## Alternatives considered
- **libvirt backend:** rejected — Linux-only in practice; kills cross-platform.
- **libvirt on Linux + direct-QMP elsewhere (hybrid):** rejected — two backends
  doubles complexity and testing for no user benefit.

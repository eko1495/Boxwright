# ADR-0003: One QEMU process per VM; auto-detect the accelerator

- **Status:** Accepted
- **Date:** 2026-05-28

## Context
Having chosen direct QEMU control (ADR-0001), we must define the runtime model:
how VMs map to processes, and how hardware acceleration is selected across
platforms whose accelerators differ (KVM on Linux, HVF on macOS, WHPX on
Windows).

## Decision
**Each VM is exactly one `qemu-system-<arch>` child process.** There is no
shared daemon or background service. A process is spawned on start and exits on
stop; Boxwright supervises it and connects to its per-process QMP endpoint.

**The accelerator is detected automatically at launch** by `AcceleratorDetector`
and **never hardcoded**:

| Host | Accelerator | Fallback |
|------|-------------|----------|
| Linux | `kvm` (if `/dev/kvm` usable) | `tcg` |
| macOS | `hvf` | `tcg` |
| Windows | `whpx` (if WHPX feature present) | `tcg` |

The resolved accelerator is shown in the UI so users understand their
performance. `accelerator: "auto"` is what we persist in config — never a
concrete value like `"kvm"`, which would not be portable.

## Consequences
- **Easier:** trivial isolation and lifecycle (kill a process = stop a VM);
  crash of one VM cannot take down others; backups are just files.
- **Easier:** no privileged daemon, so the default path needs no admin/root.
- **Harder:** we manage N processes, N QMP connections, and N log streams
  ourselves. Straightforward with `System.Diagnostics.Process` + async QMP.
- **Reality:** WHPX on Windows is genuinely slower than VMware/VirtualBox. The
  model cannot change that; we are honest about it (see `architecture.md` §5).

## Alternatives considered
- **A managing daemon/service** (libvirt-style): rejected — adds installation
  friction and privilege requirements; contradicts ADR-0001 and ADR-0006.
- **Hardcoding KVM with manual overrides:** rejected — breaks macOS/Windows out
  of the box.

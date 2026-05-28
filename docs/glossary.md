# Glossary

Domain terms used throughout the codebase and docs. Skim this before backend
work so the vocabulary in `architecture.md` and the ADRs is unambiguous.

| Term | Meaning |
|------|---------|
| **QEMU** | The virtualization/emulation engine that does the actual work. Boxwright launches `qemu-system-<arch>` processes and never reimplements them. GPLv2-licensed. |
| **QMP** (QEMU Machine Protocol) | JSON-RPC-style protocol over a socket for controlling a running QEMU at the machine level (power actions, queries, hotplug, snapshots, events). Our `Boxwright.Qmp` library speaks this. Spec: https://www.qemu.org/docs/master/interop/qmp-spec.html |
| **HMP** (Human Monitor Protocol) | The human-typed monitor (`(qemu)` prompt). We use **QMP**, not HMP, for programmatic control. |
| **QAPI** | QEMU's schema/IDL that defines QMP commands and types. `query-qmp-schema` returns it at runtime for capability detection. |
| **QGA** (QEMU Guest Agent) | An agent **inside the guest** (over virtio-serial) enabling clean shutdown, IP reporting, and file/exec helpers. Optional. |
| **`qemu-img`** | QEMU's disk-image CLI tool. `DiskService` wraps it for create/info/snapshot/convert. |
| **qcow2** | QEMU Copy-On-Write v2 disk format. Supports sparse allocation, internal snapshots, compression. Boxwright's default disk format. |
| **virtio** | Paravirtualized device family (virtio-net, virtio-blk, virtio-scsi, virtio-serial). Faster than emulated hardware. Used by default; Windows guests need the **virtio-win** driver ISO. |
| **virtio-win** | Signed Windows driver ISO for virtio devices. Boxwright auto-attaches it when creating a Windows guest. |
| **SPICE** | Remote-display protocol with clipboard, folder sharing, multi-monitor, and USB redirection. QEMU can act as a SPICE server (`-spice`). MVP launches the external `remote-viewer` client; v1.0 embeds a client. |
| **spice-vdagent** | In-guest agent for SPICE clipboard sync and dynamic resolution. |
| **`remote-viewer`** | A SPICE/VNC client shipped in the **virt-viewer** package. Boxwright shells out to it for the MVP display. |
| **VNC** | Simple, universal remote-framebuffer protocol (`-vnc`). Pure-C# clients are feasible; planned as the first *embedded* display (v0.3). Lacks SPICE's sharing features. |
| **Accelerator** | The hardware-virtualization backend QEMU uses (`-accel …`). Auto-detected per host; never hardcoded. |
| **KVM** | Kernel-based Virtual Machine — the Linux accelerator (`-accel kvm`). Needs `/dev/kvm`. |
| **HVF** | Hypervisor.framework — the macOS accelerator (`-accel hvf`), excellent on Apple Silicon. Needs the hypervisor entitlement on a signed app. |
| **WHPX** | Windows Hypervisor Platform — the Windows accelerator (`-accel whpx`). Workable but slower than VMware/VirtualBox; the WHPX feature must be enabled; may need `kernel-irqchip=off`. |
| **HAXM** | Intel's old Windows/macOS accelerator. **Discontinued Jan 2023 — do not use.** |
| **TCG** (Tiny Code Generator) | QEMU's pure-software emulation (`-accel tcg`). The universal fallback; slow; also how foreign-architecture guests run. |
| **SLIRP / user-mode networking** | `-netdev user` NAT networking needing no admin/root. Boxwright's default network mode. |
| **TAP** | A host network tap device for bridged networking (`-netdev tap`), making the guest visible on the LAN. OS-specific, often privileged; gated advanced feature. |
| **hostfwd** | SLIRP port-forwarding (e.g. host:2222 → guest:22). |
| **UEFI / OVMF** | UEFI firmware for guests (vs. legacy BIOS/SeaBIOS). Selectable per VM. |
| **`-smp`** | QEMU CPU topology argument (sockets/cores/threads). Boot-time only; not changeable via QMP. |
| **libvirt** | A Linux virtualization-management daemon + API. **Deliberately NOT used** by Boxwright — it is effectively Linux-only and would break cross-platform parity (see ADR-0001). |
| **ADR** | Architecture Decision Record — a short doc capturing one decision and its rationale. See `docs/adr/`. |
| **MVVM** | Model-View-ViewModel — the UI pattern in `Boxwright.App` (via CommunityToolkit.Mvvm). |

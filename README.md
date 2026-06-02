<div align="center">

# Boxwright

**A cross-platform desktop GUI for QEMU — like VirtualBox, powered by QEMU.**

Windows · macOS · Linux · open source (MIT) · no daemon · no Oracle account · no Broadcom portal

</div>

> ⚠️ **Early / pre-release.** Boxwright is under active early development and is
> not yet ready for general use. APIs, config formats, and features will change.
> `Boxwright` is a working codename.

---

## What is it?

Boxwright is a friendly graphical front-end for [QEMU](https://www.qemu.org/).
It gives you the ease of "pick an OS, click, boot" together with a clean
VirtualBox-style interface — and it behaves the **same on every desktop OS**.
QEMU does the virtualization; Boxwright makes it pleasant.

It controls QEMU directly (no `libvirt`, no background daemon): every VM is a
single QEMU process described by a single human-readable JSON file you can copy,
back up, or move between machines.

## Why another VM GUI?

The good options each cover one corner:

- **virt-manager** — powerful, but Linux-only and tied to the libvirt daemon.
- **GNOME Boxes** — simple, but Linux-only and very limited.
- **UTM** — beautiful, but macOS/iOS-only.
- **Quickemu** — wonderful "one-command any OS," but a Linux-first CLI.
- **VirtualBox** — polished, but Oracle-owned with the Extension Pack licensing
  asterisk.
- **VMware Workstation/Fusion** — now free, but closed source and gated behind
  the Broadcom support portal.

**No actively-maintained tool gives you a polished, beginner-friendly QEMU GUI
on Windows *and* macOS *and* Linux.** That gap is what Boxwright aims at.

## Planned features

- ✅ Create, configure, start/stop/pause/reset, and delete VMs
- ✅ Sensible defaults (virtio everywhere, qcow2 disks, UEFI)
- ✅ Automatic accelerator selection — KVM (Linux), HVF (macOS), WHPX (Windows),
  TCG fallback
- ✅ ISO mount & boot; SPICE display via the system viewer
- 🚧 One-click OS catalog (download & boot popular distros / Windows eval)
- 🚧 Auto-attach virtio-win drivers for Windows guests
- 🚧 Snapshots
- ✅ Embedded display — render a VNC guest in-app (set a VM's display protocol to VNC). Best as a
  quick **console**; VNC is laggy for heavy graphical use, so SPICE + remote-viewer stays the smooth
  option. 🔜 embedded SPICE (clipboard / folder / USB sharing)
- 🔜 USB passthrough, bridged networking, performance graphs

See [`docs/roadmap.md`](docs/roadmap.md) for the full plan.

## Download & install

> Pre-release — expect rough edges. Not code-signed yet.

### Windows

1. Download the latest `Boxwright-<version>-win-x64.zip` from the
   [Releases](https://github.com/eko1495/Boxwright/releases) page.
2. Extract it anywhere and run `Boxwright.App.exe`. It is **self-contained** (no .NET
   install needed) and **QEMU is bundled** (no QEMU install needed).
3. It isn't code-signed yet, so SmartScreen may warn — click **More info → Run anyway**.
4. To *view* a running VM, install **virt-viewer** once (it isn't bundled):
   [virt-manager.org/download](https://virt-manager.org/download/). VMs run without it;
   only the display window needs it.

### Linux

1. Download the latest `Boxwright-<version>-x86_64.AppImage` from the
   [Releases](https://github.com/eko1495/Boxwright/releases) page.
2. Make it executable and run it: `chmod +x Boxwright-*.AppImage && ./Boxwright-*.AppImage`.
   It is **self-contained** (no .NET install needed).
3. **QEMU and the viewer aren't bundled** on Linux — install them once, e.g.
   `sudo apt install qemu-system-x86 qemu-utils virt-viewer` (Debian/Ubuntu) or
   `sudo dnf install qemu-system-x86 qemu-img virt-viewer` (Fedora). For KVM acceleration,
   your user needs access to `/dev/kvm` (usually the `kvm` group).

See [`packaging/README-FIRST.txt`](packaging/README-FIRST.txt) (Windows) and
[`packaging/README-FIRST-linux.txt`](packaging/README-FIRST-linux.txt) (Linux) for the same
notes. On Windows, QEMU is slower than VMware/VirtualBox — see the performance note below.

## Honest note on performance

On **Linux** (KVM) and **Apple Silicon macOS** (HVF), QEMU is fast. On
**Windows**, QEMU uses WHPX and is **genuinely slower than VMware/VirtualBox** —
that is an upstream QEMU/WHPX reality a GUI cannot change. Boxwright's advantage
is breadth, openness, and ease of use across all three platforms, not raw speed
on any single one.

## Building from source

Requires the .NET SDK (current LTS) and, for running VMs during development, a
QEMU install on `PATH`.

```bash
git clone https://github.com/eko1495/Boxwright.git
cd Boxwright

# first-time solution scaffolding (until checked-in projects exist)
dotnet new sln -n Boxwright
dotnet sln add src/Boxwright.Qmp src/Boxwright.Core src/Boxwright.App \
               tests/Boxwright.Qmp.Tests tests/Boxwright.Core.Tests

dotnet build
dotnet test
dotnet run --project src/Boxwright.App
```

## Repository layout

```
src/Boxwright.Qmp    QEMU Machine Protocol client (also published to NuGet)
src/Boxwright.Core   VM model, config, process management, accelerator detection
src/Boxwright.App    Avalonia GUI (MVVM)
docs/                architecture, roadmap, conventions, and ADRs
```

## Documentation

- [Architecture](docs/architecture.md) — how it works and why
- [Roadmap](docs/roadmap.md) — phased plan and decision gates
- [Architecture Decision Records](docs/adr/README.md) — the rationale behind key
  choices
- [Contributing](CONTRIBUTING.md) · [Conventions](docs/conventions.md) ·
  [Glossary](docs/glossary.md)

## License

[MIT](LICENSE). QEMU itself is GPLv2 and is invoked as a separate program; when
distributed, bundled QEMU binaries are shipped unmodified with their
corresponding source. See [ADR-0005](docs/adr/0005-gpl-hygiene-subprocess.md).

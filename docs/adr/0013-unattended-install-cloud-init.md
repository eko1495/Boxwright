# ADR-0013: Unattended install via a cloud-init NoCloud seed (Ubuntu autoinstall)

- **Status:** Accepted
- **Date:** 2026-06-03

## Context
The OS catalog (ADR-0010) downloads an ISO, creates a VM with it attached, and boots the **interactive**
installer — the user still clicks through the whole install. The "Quickemu moment" (roadmap Stage 2) is
*pick it, click, walk away*: pre-answer the installer so it runs itself. "cloud-init" only means **Ubuntu**
here — subiquity autoinstall reads a cloud-init **NoCloud** datasource; Debian uses preseed, Fedora uses
kickstart, Mint's Ubiquity has no robust unattended path. So the honest scope is Ubuntu, capability-gated.

A QEMU fact shapes the design: `-append` (kernel cmdline) is only honoured with `-kernel`. A `CIDATA`-labelled
seed alone gets cloud-init to **discover and apply** the autoinstall config (it probes the label, no cmdline
needed), but subiquity still shows **one** disk-erase confirmation unless `autoinstall` is on the kernel
cmdline — which would require extracting kernel+initrd from the ISO.

## Decision
- **Ubuntu-only for the MVP** (Desktop + Server, 24.04 — unified subiquity). Catalog entries carry `osFamily`
  and `supportsAutoinstall`; only Ubuntu is `true`. Debian/Fedora/Mint stay interactive and the UI says so
  (Directive 4 — degrade gracefully, never silently).
- **The seed is a FAT image labelled `CIDATA`, not ISO9660.** The only managed ISO writer (DiscUtils) appends
  a trailing `.` to extension-less names (`user-data` → `user-data.`) and cannot emit Rock Ridge, so cloud-init
  would not find the files. A FAT volume preserves the exact names, and NoCloud explicitly supports a FAT seed
  (this is what `cloud-localds` produces). Built in pure managed code via **`DiscUtils.Fat`** — no
  `genisoimage`/`xorriso` — so it works identically on Windows, macOS, and Linux.
- **Attached as a tiny raw virtio disk** (`seed.img`) in the VM folder. The autoinstall storage layout uses
  `match: { size: largest }` so the installer targets the real disk, never the seed.
- **Password handling:** `identity.password` needs a crypt(3) hash; the BCL has none, so a small vector-tested
  `$6$` (SHA-512 crypt) helper lives in Core. The plaintext never reaches the seed.
- **First third-party functional dependency in `Boxwright.Core`** (DiscUtils, MIT). Deliberate and contained —
  it is managed and cross-platform, and violates no directive (not Avalonia, not libvirt, not QEMU-linking).
- **Phased hands-free.** **Phase A (this ADR): "pre-filled, confirm-once"** — the seed alone; no kernel cmdline,
  no kernel/initrd extraction; the user may confirm the disk-erase once in the guest. **Phase B (future, gated
  on validation):** extract `/casper/vmlinuz` + `/casper/initrd` and boot
  `-kernel`/`-initrd`/`-append "autoinstall ds=nocloud"` for true zero-touch — only if Phase A's smoke shows
  confirm-once isn't good enough.
- **Embedded SPICE was evaluated this cycle and deferred** (recorded here so the analysis isn't lost): there is
  no maintained .NET SPICE client; FFI to `libspice-client-glib` drags a heavy native dependency tree that is
  fragile to bundle on Windows/macOS (threatening Directive 4); the pure-Rust client is GPL v3 (blocked by
  ADR-0005); a from-scratch C# client is multi-month; and remote-viewer already delivers smooth SPICE. It
  remains the v1.0 target per ADR-0004/0008/0012.

This **extends** ADR-0010 (the catalog); it supersedes nothing.

## Consequences
- **Easier:** one-click *unattended* Ubuntu installs on top of the existing catalog flow; a cross-platform seed
  with no external tools; the gating makes the per-distro reality visible instead of silently broken.
- **Harder / accepted:** Ubuntu-only for now (others are honestly gated); the seed persists as a tiny extra
  disk after install (a post-install detach is a follow-up); the first third-party dependency in Core.
- **Honest (Directive 9):** Phase A is **pre-filled, confirm-once**, not yet zero-touch — UI copy says as much,
  and how hands-free it actually is must be confirmed by the dev-box smoke before any "fully automated" claim.

## Alternatives considered
- **ISO9660 seed on a CD-ROM** (the obvious "second cdrom"): rejected — DiscUtils mangles the names and writes
  no Rock Ridge, so cloud-init wouldn't find `user-data`/`meta-data`. FAT-as-a-disk is the reliable path.
- **Hand-rolled ISO9660 + Rock Ridge** or a different ISO library: more code/risk for no gain over FAT.
- **NoCloud-net over HTTP** (a local `HttpListener` + `ds=nocloud-net`): needs the kernel cmdline and a running
  server — more moving parts than a labelled volume.
- **Cover Debian/Fedora/Mint now** (preseed/kickstart/Ubiquity): genuinely different mechanisms; deferred and
  capability-gated rather than faked.

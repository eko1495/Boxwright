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
- **Phased hands-free.** **Phase A (this ADR): the seed alone, no kernel cmdline.** Validated on the dev box
  (2026-06-03): the seed is generated + attached correctly, but Ubuntu 24.04's **live-server installer ignores
  a NoCloud seed without `autoinstall` on the kernel command line** — it boots the normal interactive installer.
  So the option **ships opt-in/off and labelled experimental**: it writes a correct seed but does not yet make
  the install hands-free. **Phase B (required to actually deliver it):** extract `/casper/vmlinuz` +
  `/casper/initrd` and boot `-kernel`/`-initrd`/`-append "autoinstall ds=nocloud"` (stateful — first boot only).
  A cleaner alternative is to switch Ubuntu to its **cloud image** (a pre-installed qcow2 that consumes the
  NoCloud seed on first boot with no installer and no kernel arg). Both are deferred.
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
- **Honest (Directive 9):** the dev-box smoke showed the seed alone does **not** trigger autoinstall on the
  24.04 live-server (you get the manual installer). So the feature ships **opt-in/off and experimental** — it
  generates a correct seed but does not yet automate the install; that needs Phase B (or cloud images). No
  "automated install" claim until it actually does.

## Alternatives considered
- **ISO9660 seed on a CD-ROM** (the obvious "second cdrom"): rejected — DiscUtils mangles the names and writes
  no Rock Ridge, so cloud-init wouldn't find `user-data`/`meta-data`. FAT-as-a-disk is the reliable path.
- **Hand-rolled ISO9660 + Rock Ridge** or a different ISO library: more code/risk for no gain over FAT.
- **NoCloud-net over HTTP** (a local `HttpListener` + `ds=nocloud-net`): needs the kernel cmdline and a running
  server — more moving parts than a labelled volume.
- **Cover Debian/Fedora/Mint now** (preseed/kickstart/Ubiquity): genuinely different mechanisms; deferred and
  capability-gated rather than faked.

## Update (2026-06-04): the cloud-image path is implemented

The "cleaner alternative" floated in the Decision — boot a pre-installed Ubuntu **cloud image** instead of
running the installer — is now shipped, and it delivers the hands-free install Phase A could not:

- A new catalog field `imageKind` distinguishes an installer ISO (`iso`, the default) from a cloud image
  (`cloudimage`). The bundled catalog gains *Ubuntu Server (cloud image)* (24.04, the official
  `…-server-cloudimg-amd64.img`).
- For a cloud image the create flow downloads the qcow2, **flattens it into the VM folder** as the disk
  (`qemu-img convert` via `IDiskService.CopyAsync`, so the folder stays self-contained/portable —
  ADR-0006), grows it to the requested size (new `IDiskService.ResizeAsync` → `qemu-img resize`), and
  attaches the existing `CIDATA` seed. There is **no installer ISO and no kernel arg** — cloud-init
  consumes the seed on first boot, so it is genuinely hands-free. Boot order is disk-only (`c`).
- The seed for this path carries **plain cloud-init** (`CloudImageUserData`), not subiquity
  `autoinstall:` — a cloud image is already installed, so the seed only creates the login. Because a
  cloud image ships no default password, the credentials are **required**, not opt-in.
- `ISeedGenerator.Generate` gained a `SeedProfile` (`InstallerAutoinstall` | `CloudImage`) that selects
  which `user-data` is baked in; the FAT/`CIDATA` mechanics (DiscUtils) are unchanged.

**Validated end-to-end (real QEMU + the Ubuntu 24.04 cloud image), and it surfaced a seed bug.** The
first boot came up with `DataSourceNone` — cloud-init never found the seed. Root cause: DiscUtils writes
the FAT volume label only into the BPB boot sector, which Linux `blkid` reports as `LABEL_FATBOOT`, **not**
`LABEL`. udev creates `/dev/disk/by-label/CIDATA` only from the real `LABEL` (a root-directory
volume-label entry, attribute `0x08`), which DiscUtils never writes — so NoCloud's label probe missed the
seed. `CloudInitSeedGenerator` now injects that root-directory `0x08` entry after formatting; a re-run then
booted with `datasource: nocloud`, the seed's hostname/user applied, and password SSH login worked. This
gotcha applies to the autoinstall seed too (Phase B), not just cloud images.

## Update (2026-06-05): Phase B (kernel direct-boot autoinstall) is implemented

Live testing of the Phase-A seed surfaced the gap precisely: the Ubuntu **Desktop** installer *read* the
`CIDATA` seed (it rendered Boxwright's exact config on its "Review your choices" screen) but still stopped
and asked the user to click **Install** — subiquity won't auto-confirm a destructive disk wipe without the
literal `autoinstall` token on the **kernel command line**. Phase B supplies it:

- New optional `VmConfig.InstallBoot` (`InstallBootConfig { KernelFile, InitrdFile, Append }`) — its
  presence makes `CommandLineBuilder` emit `-kernel/-initrd/-append`; clearing it returns the VM to a
  normal disk boot. Optional field → backward-compatible, no schema bump.
- New `InstallMediaExtractor` (reuses the existing `DiscUtils.Iso9660` **`CDReader`** — pure managed, no
  external tool) extracts `/casper/vmlinuz` + `/casper/initrd` into the VM folder and reads the ISO's own
  `linux /casper/vmlinuz …` line from `/boot/grub/grub.cfg`, prepending `autoinstall ds=nocloud` (so the
  desktop ISO's `layerfs-path=…` is preserved).
- The catalog's ISO-autoinstall path now extracts on create and sets `InstallBoot`. When the install
  finishes and the guest powers off (the seed's `shutdown -P now`), `VmListItemViewModel.OnSessionExited`
  **graduates** the VM: it drops `InstallBoot`, ejects the installer media, and switches to disk-first
  boot, so subsequent starts come up off the freshly installed OS. (A user-initiated stop mid-install does
  *not* graduate — the install can be retried.)

**Verified end-to-end on real QEMU** (Ubuntu 24.04 live-server, q35/UEFI/WHPX, the real Boxwright command
line `-kernel vmlinuz -initrd initrd -append "autoinstall ds=nocloud ---"`): the UEFI EFI-stub loaded the
kernel+initrd (so `-kernel` works under OVMF), casper found the attached ISO and booted the live system,
and subiquity ran in **autoinstall mode** (`apply_autoinstall_config`, `curtin` running) — sailing past the
confirm prompt that blocked Phase A — with no interaction. This resolved the two risks ADR-0013 flagged
(UEFI direct-kernel boot; casper locating the medium). The Phase-A `LABEL_FATBOOT` seed fix above is a
prerequisite and is in place.

A cloud image (the update above) is still the lightest hands-free **Server**; Phase B is what makes a full
**Desktop/Server ISO** install hands-free. These updates **extend** the ADR; they supersede nothing.

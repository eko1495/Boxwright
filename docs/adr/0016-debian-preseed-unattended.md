# ADR-0016: Debian unattended install via an initrd-injected preseed (+ a per-family installer seam)

- **Status:** Accepted (the mechanism stands; the implementation was re-expressed as a declarative
  `initrd-inject` recipe on the bundled `debian-13-netinst` entry and the bespoke `DebianPreseedInstaller`
  deleted — see ADR-0026)
- **Date:** 2026-06-05

## Context
ADR-0013 delivered hands-free **Ubuntu** installs (cloud image; and "Phase B" ISO autoinstall that boots
the extracted casper kernel with `autoinstall` on the command line). The keystone primitive built there —
`VmConfig.InstallBoot` → `CommandLineBuilder` emitting `-kernel/-initrd/-append`, cleared once the install
powers off — is family-agnostic. **Debian** is the obvious next family, but its mechanism is different:
debian-installer (d-i) is driven by a **preseed.cfg** (a debconf answer file), not cloud-init. The Ubuntu
logic was hardcoded inline in `CatalogViewModel`; adding a second family without a seam would bloat the
view-model and entangle the families.

Two facts shaped the design:
- **Preseed delivery.** A d-i initramfs is a *concatenation of gzipped-cpio segments*, and d-i automatically
  reads `/preseed.cfg` from the initramfs root. So we can **append** one gzipped-cpio segment carrying
  `/preseed.cfg` to the installer's `initrd.gz` — no read-modify-write of the original, no external tool, no
  running HTTP server. This must use the **text** installer (`install.amd/vmlinuz` + `install.amd/initrd.gz`);
  the gtk installer does not honour an initrd preseed. `auto=true priority=critical` on the kernel command
  line suppresses the early locale/keyboard/network prompts that fire before the preseed loads.
- **Fedora can't follow yet.** Fedora's analog is kickstart, but the catalog's Fedora entry is a **Live** ISO,
  and Live media cannot run a kickstart install — that needs the **Server/netinst** edition (a new catalog
  entry + a network-repo `inst.ks`/OEMDRV path). Out of scope here; recorded as a follow-up.

## Decision
- **A per-family installer seam in Core.** `IUnattendedInstaller` (`OsFamily` + `Prepare(iso, vmFolder,
  answers) → UnattendedInstallPlan`) where `UnattendedInstallPlan` is `{ InstallBootConfig Boot;
  IReadOnlyList<DiskConfig> SeedDisks }`. `IUnattendedInstallerResolver` resolves one by
  `OsCatalogEntry.OsFamily`. `UbuntuAutoinstaller` wraps the existing `ISeedGenerator` (cloud-init `CIDATA`
  seed) + `IInstallMediaExtractor` (casper kernel) — **no behaviour change** for Ubuntu. `CatalogViewModel`'s
  unattended branch is now one family-driven call: attach `plan.SeedDisks`, set `InstallBoot = plan.Boot`.
- **`DebianPreseedInstaller`.** Extracts `install.amd/vmlinuz` + `install.amd/initrd.gz` via the shared
  `IsoMedia` helper (DiscUtils `CDReader`, refactored out of `InstallMediaExtractor`), generates a complete
  `preseed.cfg` (`DebianPreseed.Build`), and **injects** it into the copied initrd
  (`InitrdFileInjector.Append`, a hand-written gzipped one-file cpio "newc" segment). Boots with
  `auto=true priority=critical`. The preseed lives in the initrd, so **no seed disk** is attached
  (`SeedDisks = []`).
- **The preseed pre-answers everything**, including the `partman/*` disk-write confirmations — Debian's
  equivalent of subiquity's "Review your choices" gate — installs **GNOME** (`tasksel: standard,
  gnome-desktop`), creates the user as a sudoer (root login disabled; password embedded only as a SHA-512
  crypt hash via the existing `Sha512Crypt`), and **powers off** at the end
  (`debian-installer/exit/poweroff true`) so the existing finalize path (`VmListItemViewModel.OnSessionExited`,
  which keys only on `InstallBoot != null`) graduates the VM to a normal disk boot, ejecting the installer
  media. No new finalize logic.
- **Pure managed + cross-platform.** DiscUtils + `System.IO.Compression` + a hand-written cpio header — no
  `genisoimage`/`cpio`/`gzip` binaries — so it behaves identically on Windows, macOS, and Linux. Violates no
  directive (not Avalonia, not libvirt, not QEMU-linking).
- **Catalog gating.** The Debian netinst entry sets `supportsAutoinstall: true`; the generic "Install
  automatically (unattended)" opt-in already keys on that flag, so it now appears for Debian with no XAML
  change. Other families stay interactive and the UI says so (Directive 4).

This **extends** ADR-0013's Phase-B primitive; it supersedes nothing.

## Verification
Live-tested on real QEMU (Debian 13.5 netinst, q35/UEFI/WHPX, the real `DebianPreseedInstaller` output:
`-kernel vmlinuz -initrd initrd -append "auto=true priority=critical"`). Two runs, both fully hands-free with
no interaction:

- **Mechanism (GNOME, the shipped default):** the OVMF EFI-stub loaded the kernel + the preseed-injected
  initrd; d-i read the injected `/preseed.cfg` (zero locale/keyboard/network prompts); `partman-auto` ran LVM,
  **created the UEFI ESP automatically** (`Creating EFI-fat16 file system`) plus an ext4 root, and
  **auto-confirmed the destructive disk write** — the gate that blocked Ubuntu's Phase A; then `Installing the
  base system` and `Select and install software` (GNOME), ~1.7 GB written. (The GNOME desktop download is a
  large, slow step over QEMU user-mode NAT; this run was not waited to completion.)
- **Full end-to-end (`standard` task, to isolate the pipeline from the multi-GB GNOME download):** same boot,
  same hands-free partition/base-install, then `Installing GRUB` (**grub-efi** to the ESP),
  `Unmounting/ejecting installation media`, and a clean **self-poweroff** (`Requesting system poweroff` →
  `reboot: Power down`, QEMU exited 0 — the preseed's `exit/poweroff`). Booting the resulting disk alone (no
  installer) came up through the installed GRUB into `Debian GNU/Linux 13` at a `boxwright-deb login:` prompt —
  the **preseeded hostname applied**. This confirms grub-efi install, the self-poweroff that drives the
  graduate path, and a bootable installed system; the GNOME run above confirms the same pipeline drives the
  desktop task.

## Consequences
- **Easier:** Debian joins the one-click unattended set; a new family is now just one more registered
  `IUnattendedInstaller`. The seam keeps `CatalogViewModel` thin and family-neutral.
- **Harder / accepted:** the unattended Debian path pulls a full GNOME desktop over the network, so the first
  install downloads a lot and is slow (the catalog note says so honestly — Directive 9); a user-initiated stop
  mid-install leaves a half-installed disk (recreate to retry, same as Ubuntu).
- **Honest (Directive 9):** Fedora is **not** done — its Live ISO can't kickstart. It is capability-gated
  (`supportsAutoinstall: false`) and stays interactive until a Server/netinst entry + kickstart path land.

## Alternatives considered
- **preseed on a labelled FAT/seed disk** (mirror the cloud-init `CIDATA` approach): d-i has no
  auto-probe-by-label for preseed (unlike cloud-init/anaconda's `OEMDRV`); it needs `preseed/file=` pointing at
  a mounted device, which is more fragile than the initrd the installer already loads.
- **`preseed/url=` over a local `HttpListener`** (QEMU user-net gateway `10.0.2.2`): works, but adds a running
  server and lifecycle to manage — more moving parts than appending to the initrd (same reason ADR-0013
  rejected NoCloud-net).
- **Extract-modify-repack the initrd** (gunzip → cpio append → gzip): more work and risk than *appending* a
  second gzipped-cpio segment, which the kernel concatenates for free.
- **Include Fedora now:** genuinely blocked on the Live-vs-Server ISO distinction; deferred rather than faked.

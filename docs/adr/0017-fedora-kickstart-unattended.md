# ADR-0017: Fedora unattended install via an Anaconda kickstart (netinst)

- **Status:** Accepted
- **Date:** 2026-06-06

## Context
Unattended install covers Ubuntu (ADR-0013) and Debian (ADR-0016) through a per-family installer seam
(`IUnattendedInstaller`, resolved by `OsCatalogEntry.OsFamily`) on the `-kernel/-initrd/-append`
`InstallBoot` primitive. Fedora was capability-gated: the catalog's "Fedora Workstation" entry is a
**Live** ISO, and Live media has no Anaconda installer environment, so it cannot run a kickstart. Adding
Fedora therefore needs a **netinst** ISO + an Anaconda **kickstart** (`ks.cfg`) — Fedora's equivalent of
Debian's preseed. The mechanics map almost exactly onto the Debian work, so the goal is maximal reuse.

A few Fedora facts shaped the design (verified via research): the installer kernel/initrd live at
`images/pxeboot/{vmlinuz,initrd.img}`; a kickstart **injected into the initrd** and selected with
`inst.ks=file:/ks.cfg` runs Anaconda fully non-interactively; and because we boot the kernel/initrd
directly (not the ISO's own loader) the kernel command line must also carry **`inst.stage2=`** so
Anaconda can find its runtime on the netinst medium.

## Decision
- **Netinst, not Live.** Add a `fedora-44-netinst` catalog entry (Fedora *Everything* netinst,
  `osFamily: fedora`, `supportsAutoinstall: true`) alongside the existing Live entry, which stays
  interactive. The single `supportsAutoinstall` flag surfaces the existing "Install automatically" opt-in
  (no UI change), and the `fedora` family routes to the new installer.
- **`FedoraKickstartInstaller : IUnattendedInstaller`** mirrors `DebianPreseedInstaller`: extract
  `images/pxeboot/vmlinuz` + `initrd.img` via the shared `IsoMedia` reader, **inject** a generated
  `ks.cfg` into the initrd with **`InitrdFileInjector`** (the Debian `InitrdPreseedInjector`, renamed —
  it appends a gzipped one-file cpio segment, which the kernel reads regardless of the base initrd's
  xz/zstd compression), and boot `inst.ks=file:/ks.cfg`. The kickstart lives in the initrd, so **no seed
  disk** is attached (`SeedDisks = []`), exactly like Debian.
- **`inst.stage2` reuses the ISO's own label.** Rather than guess, parse the netinst's own
  `inst.stage2=hd:LABEL=…` out of its `EFI/BOOT/grub.cfg` (authoritative) and reuse it verbatim; fall
  back to `inst.stage2=hd:LABEL=<ISO volume label>` (spaces escaped `\x20`) when grub.cfg has none. This
  mirrors the Ubuntu extractor's "preserve the ISO's own kernel args" approach. `IsoMedia` gained a
  `VolumeLabel` accessor for the fallback. `inst.text` keeps Anaconda on the non-graphical installer.
- **`FedoraKickstart.Build`** emits a complete `ks.cfg`: `text`, `keyboard`/`lang`/`timezone` from
  answers, DHCP networking + hostname, `url --mirrorlist=…` (a netinst ships no packages, so the repo is
  the Fedora mirrors), `zerombr` + `clearpart --all --initlabel` + `autopart` (creates the ESP under
  UEFI), `rootpw --lock` + a `wheel` sudo user whose password is a **`Sha512Crypt` `$6$`** hash (plaintext
  never written), `@^workstation-product-environment` (GNOME), and **`poweroff`** at the end so the guest
  powers off and the existing family-agnostic `OnSessionExited` graduate path ejects media + disk-boots.
- **Pure managed, cross-platform** (DiscUtils + gzip/cpio) — no external tool. Violates no directive.

The App layer is **unchanged** — the seam already dispatches by family; this is a Core installer, one DI
registration, and a catalog entry. This **extends** ADR-0013/0016; it supersedes nothing.

## Consequences
- **Easier:** Fedora joins the one-click unattended set with heavy reuse (the seam, the `InstallBoot`
  boot, the initrd injector, `IsoMedia`, `Sha512Crypt`, the graduate path). A fourth family would be the
  same shape again.
- **Harder / accepted:** the unattended target is Workstation/GNOME over the network, so the first install
  downloads a lot and is slow (the catalog note says so — Directive 9); two Fedora rows in the catalog
  (Live = interactive, netinst = unattended); a user-initiated stop mid-install leaves a half-installed
  disk (recreate to retry, same as the others).
- **Honest (Directive 9):** the Live ISO genuinely cannot kickstart; it stays interactive and the UI says
  so, rather than faking unattended on it.

## Verification
**Live-verified end-to-end on real QEMU** (Fedora 44 Everything netinst, q35/UEFI/WHPX, the real
`FedoraKickstartInstaller.Prepare` output). `images/pxeboot/{vmlinuz,initrd.img}` were extracted and the
`ks.cfg` injected; the boot append came out as `inst.ks=file:/ks.cfg
inst.stage2=hd:LABEL=Fedora-E-dvd-x86_64-44 inst.text` — the `inst.stage2` label correctly read from the
ISO's own `EFI/BOOT/grub.cfg`, which settles the keystone risk. Booting it, **Anaconda 44.30 started, read
the kickstart, and ran a fully automated text-mode install with no prompts** ("Not asking for remote
desktop session because of an automated install" → "Starting automated install"): autopart created the
UEFI ESP, 427 `@core` packages installed, **grub2-efi** went to the ESP, users were created, and the
kickstart's `poweroff` exited QEMU (`reboot: Power down`). Booting the installed disk alone then came up
via grub-efi to `Fedora Linux 44` / `boxwright-fed login:` — the **kickstart hostname applied**. The
end-to-end run used `@core` to avoid the multi-GB GNOME download stalling over slirp NAT (as Debian's GNOME
did); the shipped kickstart differs only in `%packages` (`@^workstation-product-environment`).

## Alternatives considered
- **OEMDRV FAT seed** (Anaconda auto-loads `ks.cfg` from a volume labelled `OEMDRV`): works and reuses the
  cloud-init FAT writer, but the initrd-injection path reuses the Debian machinery exactly and needs no
  extra seed disk — fewer moving parts.
- **Boot the netinst's own loader + OEMDRV** (no `-kernel`): avoids `inst.stage2` discovery but relies on
  the ISO's 60 s GRUB timeout + media-check and doesn't fit the seam's `InstallBoot` shape.
- **`inst.ks=http://` over a local listener:** needs a running server — more moving parts (same reason
  ADR-0013 rejected NoCloud-net).
- **Fedora Server instead of Workstation:** a one-line `%packages` change; Workstation matches the
  existing Fedora entry's identity and the Debian GNOME choice.

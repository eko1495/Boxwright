# ADR-0015: Unattended Windows install via an Autounattend.xml ISO seed

- **Status:** Accepted
- **Date:** 2026-06-04

## Context
ADR-0013 (with the PR-1 cloud-image follow-up) delivers hands-free **Linux** installs. Windows is the
other OS users most want one-click, but it differs in three ways that shape the design:

1. **No clean auto-download.** Microsoft's Windows ISO links are dynamic/session-based and the
   Evaluation-Center URLs + checksums rotate, so a catalog entry would constantly go stale (the catalog,
   ADR-0010, already calls its entries "best-effort"). Windows is fundamentally **bring-your-own-ISO**.
2. **Windows Setup has no in-box virtio driver**, so it cannot see a virtio disk — the Boxwright default.
3. **Windows 11 requires TPM 2.0 + Secure Boot**, which a plain QEMU q35 VM does not provide.

## Decision
- **Bring-your-own ISO, wired into the New-VM flow.** Picking the Windows guest OS reveals an opt-in
  "Install Windows automatically (unattended)" panel: the user points at their Windows 10/11 ISO and
  enters a hostname, username, password, and an optional product key. No download, no fragile URLs.
- **Generate `Autounattend.xml` from a fixed, verified template** (`AutounattendXml`, from
  `UnattendedAnswers` + `WindowsInstallOptions`). It wipes disk 0, installs the chosen edition, creates a
  local **administrator**, and **auto-logs-on** so first boot lands on the desktop. The template is built
  from Microsoft Learn primary sources; the `<Password>` value uses the verified
  `Base64(UTF-16LE(password + "Password"))` scheme; the disk block is firmware-aware (**GPT** for UEFI,
  **MBR** for BIOS).
- **Bypass the Windows 11 hardware checks from the answer file** — a `windowsPE` `RunSynchronous`
  sequence writes `HKLM\System\Setup\LabConfig` `BypassTPMCheck`/`BypassSecureBootCheck`/`BypassRAMCheck`
  (+ Storage/CPU + `BypassNRO`). So Win11 installs on a plain q35 VM with **no vTPM / swtpm** dependency.
- **The seed is an ISO9660 (Joliet) CD** holding `Autounattend.xml` at its root
  (`AutounattendSeedGenerator`, via `DiscUtils.Iso9660`), attached as a **second CD-ROM**. Windows Setup
  auto-scans removable-media roots and applies it with no interaction. Joliet preserves the exact
  `Autounattend.xml` name — the ISO9660 trailing-dot mangling that blocked the cloud-init seed (ADR-0013)
  does not apply here because the file has an extension. (The PR-1 `LABEL_FATBOOT`/`LABEL` FAT gotcha is
  also moot — that was FAT-only.) Second managed DiscUtils dependency (MIT, cross-platform) in Core.
- **In-box drivers, not virtio-win.** A Windows guest puts storage on the q35 chipset's built-in
  **AHCI/SATA** controller (`ide-hd`/`ide-cd` on `ide.N`) and uses an **e1000e** NIC — both have in-box
  Windows drivers, so the install is hands-free with **no virtio-win ISO**. Slower I/O than virtio, which
  is an honest trade for simplicity (Windows-on-QEMU is already slow — Directive 9). The
  `CommandLineBuilder` SATA wiring was verified to be accepted by QEMU 11 on q35.
- **Disk-first boot order** (`cd`): the empty disk falls through to the installer CD on first boot, then
  boots Windows from disk afterwards (VirtualBox's reconfigure behavior).

This **extends** ADR-0013/0010; it supersedes nothing.

## Consequences
- **Easier:** one-click *unattended* Windows 10/11 from a user-supplied ISO, incl. Win11 on hardware that
  fails the official checks; a pure-managed cross-platform seed; no virtio-win, no swtpm.
- **Harder / accepted:** SATA is slower than virtio (acceptable); a second DiscUtils package; en-US/UTC
  locale defaults for now (Windows time-zone/locale customization is a follow-up); product-key/edition
  handling is best-effort (generic Pro key offered; blank for Evaluation ISOs).
- **Honest (Directive 9) — not yet verified by a live install.** The `Autounattend.xml` is built from
  Microsoft-verified sources and unit-tested for structure (well-formed, correct passes, password vector,
  GPT/MBR, Win11 bypass), and the seed ISO is verified readable (Joliet, file at root). But unlike the
  Linux path, a **real Windows install run has not been performed** (no Windows ISO was available on the
  dev box). Two known gaps to close before claiming "verified": (a) a live install to the desktop, and
  (b) the **"Press any key to boot from CD"** prompt the Windows ISO shows on first boot — until Boxwright
  auto-sends a keypress (QMP `send-key`, a follow-up), the user may need one keypress to start Setup.

## Alternatives considered
- **virtio + a bundled/auto-attached virtio-win ISO** (faster guest I/O, with `<DriverPaths>` injection):
  more moving parts (virtio-win acquisition + driver-path correctness). Deferred as a performance follow-up.
- **A FAT seed on a removable USB** (reuse the cloud-init FAT writer): works, but a CD-ISO fits the
  removable-media model more cleanly and is the standard autounattend medium (and what VirtualBox uses).
- **Downloadable Windows Evaluation catalog entry:** rejected — dynamic URLs/checksums rotate; BYO is reliable.
- **A virtual TPM (swtpm) for Win11:** rejected — extra native dependency that is fragile to bundle on
  Windows/macOS; the LabConfig bypass needs none.

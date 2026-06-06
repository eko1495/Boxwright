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

## Update (2026-06-06): auto-keypress + self-graduating install (both gaps closed)

The two gaps flagged above are now closed, so the Windows install is genuinely hands-free and self-cleaning
— matching the Linux flow:

- **Auto-keypress (QMP `send-key`).** A new `IQmpClient.SendKeyAsync` (qcode chord) → `IRunningVm.SendKeysAsync`
  primitive lets the app press a guest key. A `VmConfig.WindowsInstallInProgress` flag (the Windows analogue
  of `InstallBoot`) is set on the unattended Windows VM; on start, `VmListItemViewModel` fires Enter once a
  second for ~12 s, which dismisses the firmware's **"Press any key to boot from CD…"** so Setup starts with
  no human present. The window is short on purpose: Windows Setup's many *in-process* reboots (QEMU stays
  alive — the builder emits no `-no-reboot`) get no keypress and fall through the prompt to the now-bootable
  disk.
- **Self-graduate.** The `Autounattend.xml` `oobeSystem` pass gains a `<FirstLogonCommands>` `shutdown /s`,
  so once Setup reaches the desktop the guest powers off and QEMU exits — the Windows analogue of the Linux
  seed's `shutdown -P now`. `VmListItemViewModel.OnSessionExited` then graduates the VM (clear the flag,
  eject the install media, switch to disk-first boot); a deliberate mid-install stop does **not** graduate
  (the existing `Status is Stopped/Stopping` guard). So the first start runs the whole install and powers
  off ("start the VM to use it"); the next start boots straight into Windows.

The QMP `send-key` wrapper stays in `Boxwright.Qmp` (Directive 8) and is tested against the fake loopback
server. The **virtio-win performance path remains the deferred follow-up** (see Alternatives).

### Live testing (Windows 11 25H2 consumer ISO, q35/UEFI/WHPX) — two real blockers surfaced

The send-key + graduate code is unit-tested and builds clean, but a live run uncovered two issues that mean
the Windows path is **not yet fully hands-free on a modern ISO**, and both are honestly recorded here
(Directive 9):

1. **OVMF "Press any key to boot from CD" is racy to auto-dismiss.** The auto-keypress *did* boot Windows
   Setup from the CD with no human in one run — the mechanism works — but the firmware prompt appears only
   after POST (observed ~15-25 s, and the exact moment varies run-to-run) and a blind timed `send-key`
   stream landed inconsistently (other runs timed the prompt out and fell through to PXE). Widening the
   keypress window (to ~45 s) helps but is not reliable on its own; a robust fix (e.g. a longer key
   `hold-time`, or detecting the prompt before pressing) is a **follow-up**.
2. **Windows 11 24H2/25H2 "ConX" setup ignores the `oobeSystem` unattend.** The redesigned setup
   (`SetupPrep.exe`) applies the `windowsPE` pass (disk wipe/partition) but hands OOBE to a new code path
   that does **not** consume the `oobeSystem` settings — so Setup stops at interactive screens (Product key,
   account) and never reaches the `FirstLogonCommands` shutdown that triggers the graduate. This is a
   Microsoft setup change affecting *all* Autounattend tooling, not specific to Boxwright. The documented
   workaround is to force the legacy setup (`winpeshl.ini` → `setup.exe /legacy`, which requires editing
   `boot.wim` inside the ISO) — a separate, larger feature. **Deferred.**

Net: the `send-key` primitive, the `WindowsInstallInProgress` keypress trigger, and the self-graduate path
(the same `OnSessionExited` path already live-verified end-to-end for Linux) are in place and unit-tested,
and the `FirstLogonCommands` shutdown still works on a legacy-setup (pre-24H2) ISO. A **fully hands-free,
end-to-end-verified Windows install on 24H2/25H2 is blocked** by the two items above, both tracked as
follow-ups. No "verified hands-free on current Windows" claim is made until they land.

## Alternatives considered
- **virtio + a bundled/auto-attached virtio-win ISO** (faster guest I/O, with `<DriverPaths>` injection):
  more moving parts (virtio-win acquisition + driver-path correctness). Deferred as a performance follow-up.
- **A FAT seed on a removable USB** (reuse the cloud-init FAT writer): works, but a CD-ISO fits the
  removable-media model more cleanly and is the standard autounattend medium (and what VirtualBox uses).
- **Downloadable Windows Evaluation catalog entry:** rejected — dynamic URLs/checksums rotate; BYO is reliable.
- **A virtual TPM (swtpm) for Win11:** rejected — extra native dependency that is fragile to bundle on
  Windows/macOS; the LabConfig bypass needs none.

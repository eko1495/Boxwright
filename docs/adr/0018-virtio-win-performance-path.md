# ADR-0018: virtio-win performance path for unattended Windows installs

- **Status:** Accepted
- **Date:** 2026-06-06

## Context
ADR-0015 installs Windows on **in-box** devices — SATA/AHCI storage + an e1000e NIC — so Setup is
hands-free with no extra drivers. The tradeoff is speed: emulated AHCI/e1000e are much slower and
higher-CPU than QEMU's paravirtualized **virtio** devices (a virtio-blk disk is commonly ~2–5× the
throughput of emulated SATA). Linux guests already use virtio everywhere; Windows can't out of the box,
because Setup ships no in-box virtio driver and so can't even see a virtio disk. The **virtio-win**
project (Red Hat / Fedora) packages the signed Windows drivers as an ISO. This adds an **opt-in** faster
path to the Windows unattended flow.

## Decision
- **Opt-in, SATA stays the default.** A "Use virtio drivers (faster disk + network)" checkbox in the
  New-VM Windows panel. Off → unchanged in-box SATA + e1000e, no download. On → the disk becomes
  **virtio-blk**, the NIC **virtio-net**, and the virtio-win driver ISO is attached + injected.
- **Auto-download + cache the virtio-win ISO**, with a bring-your-own override. `VirtioWin` pins a
  versioned ISO (0.1.285, URL + size + a SHA-256 we hash once and pin — upstream publishes none for the
  ISO) and exposes a synthetic `OsCatalogEntry` so the existing `IIsoDownloader.EnsureAsync`
  downloads/verifies/caches it in the same ISO cache — no new download code. The pin is best-effort and
  rotates like the catalog entries.
- **virtio-blk disk, CDs stay on SATA.** `CommandLineBuilder.AppendWindowsStorage` emits
  `virtio-blk-pci` for a `virtio`-interface disk (otherwise the existing `ide-hd` on AHCI). The CD-ROMs
  (Windows ISO, virtio-win ISO, autounattend seed) stay on the AHCI controller so the Windows ISO is
  firmware-bootable and WinPE-readable with the in-box driver. The NIC needs no builder change —
  `AppendNetworking` already emits `Network.Model` verbatim.
- **Driver injection in the Autounattend.** A `WindowsInstallOptions.UseVirtio` flag makes `AutounattendXml`
  add `Microsoft-Windows-PnpCustomizationsWinPE` `DriverPaths` for **viostor** (so WinPE sees the
  virtio-blk disk) and an `offlineServicing` `Microsoft-Windows-PnpCustomizationsNonWinPE` `DriverPaths`
  for **viostor + NetKVM** (so the installed OS boots from virtio and has network). The virtio-win CD's
  WinPE drive letter is unpredictable, so each path is emitted for several **candidate letters (D:–G:)** ×
  driver — Setup silently ignores the ones that don't resolve. Targets the Windows 11 `w11\amd64`
  drivers.

This **extends** ADR-0015; it supersedes nothing. It is pure performance polish — Windows already
installs fine on SATA.

## Consequences
- **Easier:** noticeably faster disk + network for Windows guests when the user opts in.
- **Harder / accepted:** a one-time ~750 MB virtio-win download (cached after); the DriverPaths
  candidate-letter approach is a pragmatic workaround for WinPE's unpredictable letters; the driver pin is
  best-effort and may need refreshing; `w11` is the targeted folder (a Win10 BYO ISO would need `w10`).

## Verification
- **Unit:** `CommandLineBuilderTests` — a `virtio` disk on a Windows VM emits `virtio-blk-pci` and the CDs
  stay `ide-cd` (starting at `ide.0`, since virtio consumes no SATA port); `virtio-net` NIC.
  `AutounattendXmlTests` — `UseVirtio` adds the windowsPE viostor + offlineServicing viostor/NetKVM
  DriverPaths (candidate letters); off adds none; still well-formed. `NewVmViewModelTests` — the virtio
  flow downloads the pinned ISO, attaches it as a 3rd CD, sets the virtio disk + NIC, and generates the
  Autounattend with `UseVirtio`; a bring-your-own path skips the download. `VirtioWinTests`.
- **Live (partial — real QEMU/WHPX/UEFI, consumer 25H2 ISO):** booted the **real** virtio command line
  (`virtio-blk-pci` disk + `virtio-net` + the three CDs incl. the cached virtio-win ISO) with the real
  `UseVirtio` Autounattend. QEMU **accepted the virtio devices** (no device errors; the firmware
  enumerated the virtio-blk disk as a boot candidate) and **WinPE/Setup booted fine** with them present —
  i.e. the virtio plumbing doesn't break the install boot. The full payoff — *Windows completing the
  install onto the virtio disk* — **can't be verified on the consumer 25H2 ISO**, because the ConX OOBE
  wall (ADR-0015) stops Setup at the interactive screens before the disk phase; that needs an
  **Enterprise/Education/LTSC** ISO (none on the box). Documented, not faked.

# Roadmap

A phased plan from "does QMP feel right in C#" to a real cross-platform v1.0.
Each phase has an exit gate. The gates are honest decision points, not
formalities — the point of a learning/community project is to stop or pivot
deliberately, not drift.

Scope discipline is the #1 survival factor for solo VM-GUI projects (AQEMU and
QtEmu both died of stall, not of bad ideas). **Resist scope creep. No
clustering, no oVirt-style fleet management, ever.**

---

## Stage 0 — Validate the foundation (one evening)

Prove the core interaction is ergonomic before committing to the architecture.

- [ ] From a throwaway C# console app on Linux, launch
      `qemu-system-x86_64 -accel kvm -m 2048 -qmp tcp:127.0.0.1:4444,server,nowait …`.
- [ ] Connect a `TcpClient`, send `{"execute":"qmp_capabilities"}`, then
      `{"execute":"query-status"}`, and parse the reply with `System.Text.Json`.
- [ ] Repeat on Windows 11 with a qemu.weilnetz.de build and `-accel whpx`;
      confirm WHPX is usable on the target machine.

**Gate:** if the QMP round-trip does not feel clean in C# within ~2 hours,
reconsider the stack before going further.

---

## Stage 1 — MVP (≈ months 1–3, weekends)

The smallest version a stranger can use. **Windows must work on day one** — that
is the strategic wedge (the gap is biggest there).

**`Boxwright.Qmp`**
- [ ] `IQmpClient` with connect + handshake, correlated `execute`, event stream.
- [ ] `query-qmp-schema` capability probe.
- [ ] xUnit tests against a fake loopback QMP server (no live QEMU).
- [ ] **Publish `Boxwright.Qmp` to NuGet** as soon as it works — instant
      ecosystem contribution and a discovery channel.

**`Boxwright.Core`**
- [ ] `VmConfig` model + JSON load/save (schemaVersion 1).
- [ ] `CommandLineBuilder` (config → args), virtio-by-default.
- [ ] `AcceleratorDetector` (kvm/hvf/whpx/tcg auto-select).
- [ ] `QemuProcess` spawn/supervise + per-VM log capture.
- [ ] `DiskService` create/info via `qemu-img` (qcow2 default).
- [ ] `DisplayLauncher` that starts `remote-viewer` against the VM's SPICE port.

**`Boxwright.App` (Avalonia)**
- [ ] VM list (create / start / stop / pause / reset / delete).
- [ ] New-VM flow with sensible defaults (RAM, cores, disk size, firmware).
- [ ] ISO mount + boot.
- [ ] Settings panel for an existing VM.
- [ ] "Open display" button → launches the SPICE viewer.

**Packaging**
- [ ] Bundle QEMU on Windows; system/Flatpak QEMU on Linux; signed QEMU in the
      macOS `.app`.
- [ ] Installable artifact per OS (MSI/zip, AppImage/Flatpak, signed `.app`).

**Exit gate (go/no-go):** *Can a stranger install it on Windows and boot Ubuntu
in under 10 minutes?* If yes → ship publicly and move to Stage 2. If no → fix
that before adding anything.

---

## Stage 2 — Find the wedge (≈ months 4–6)

This is the "Quickemu moment" — the feature most likely to attract stars.

- [x] **Built-in OS catalog** with one-click ISO download — our own bundled JSON
      behind an interface, now fronted by a **remote, community-maintainable
      manifest** (remote → last-good cache → bundled, best-effort; the catalog grows
      and refreshes without an app update). First cut: Ubuntu 24.04 Desktop/Server,
      Debian 13 netinst; Fedora/Mint/Windows to follow (Windows needs a non-direct
      acquisition flow). See ADR-0010 and ADR-0020.
- [x] Checksum (SHA-256) verification + provenance display for downloads.
      GPG/PGP *signature* verification is a fast-follow.
- **Unattended install** for Ubuntu — two paths (see ADR-0013):
  - [x] *Cloud image* — the pre-installed `…-server-cloudimg-amd64.img` is flattened into the VM disk
        (`qemu-img convert`), grown to size (`qemu-img resize`), and booted with the cloud-init
        `CIDATA` seed. Runs hands-free on first boot — **no installer, no kernel arg**. This is the
        "pick it, click, walk away" path; credentials are required (a cloud image has no default login).
  - [x] *Desktop/Server ISO autoinstall* (Phase B) — `InstallMediaExtractor` pulls the ISO's
        `vmlinuz`/`initrd` and the VM boots `-kernel/-initrd -append "autoinstall …"`, so subiquity runs
        fully non-interactively (no "Review your choices" prompt); the one-shot kernel boot is dropped once
        the install powers off. Verified end-to-end on real QEMU (q35/UEFI).
- **Unattended install** for Debian (see ADR-0016):
  - [x] *Netinst preseed* — a per-family installer seam (`IUnattendedInstaller`, resolved by OS family)
        adds Debian: `DebianPreseedInstaller` extracts the text installer's `install.amd/vmlinuz` +
        `initrd.gz`, **injects** a generated `preseed.cfg` into the initrd (a gzipped cpio segment — no
        external tool), and boots `-append "auto=true priority=critical"` so d-i runs fully non-interactively
        (auto-confirming the partman disk-write prompts). Installs a GNOME desktop and powers off, after which
        the VM graduates to a disk boot (same finalize path as Ubuntu).
- **Unattended install** for Fedora (see ADR-0017):
  - [x] *Netinst kickstart* — a new Fedora **Everything netinst** catalog entry routes (by `osFamily`) to
        `FedoraKickstartInstaller`, which extracts `images/pxeboot/{vmlinuz,initrd.img}`, **injects** a
        generated `ks.cfg` into the initrd (same primitive as Debian), and boots
        `-append "inst.ks=file:/ks.cfg inst.stage2=hd:LABEL=… inst.text"` so Anaconda runs fully
        non-interactively. Installs the GNOME (Workstation) environment and powers off → graduates like the
        others. The Fedora **Live** entry stays interactive (Live media can't kickstart).
- **Unattended Windows** install (see ADR-0015):
  - [x] *Bring-your-own ISO* — pick a Windows 10/11 ISO in the New-VM flow + credentials; Boxwright bakes
        an `Autounattend.xml` seed CD (local admin + auto-login), bypasses the Win11 TPM/Secure-Boot
        checks (no vTPM), and uses in-box **SATA + e1000e** so Setup needs no virtio-win. Built + unit-tested.
  - [x] *Hands-free + self-graduating* (ADR-0015 + updates) — a **held-key** auto-keypress (QMP
        `input-send-event`, driven by a `WindowsInstallInProgress` flag) reliably dismisses the firmware's
        "Press any key to boot from CD" (verified 3/3 on QEMU/OVMF, where discrete presses raced and missed),
        and an Autounattend that creates the account in the **specialize** pass + a `FirstLogonCommands`
        shutdown so the VM graduates (eject media, disk-boot) via the same `OnSessionExited` path as Linux.
        Fully hands-free on **Enterprise/Education/LTSC + legacy/pre-24H2** ISOs.
  - [ ] *Consumer 24H2/25H2 (ConX)* — Microsoft's new Setup ignores the oobeSystem unattend on **consumer
        Home/Pro**, so it stops at interactive account setup; a Microsoft limitation (confirmed live). Use an
        Enterprise/Education/LTSC or pre-24H2 ISO for fully hands-free. The "force legacy setup" boot.wim
        slipstream was deliberately not built (out-of-ethos: native WIM tooling + read-only-media rebuild).
  - [x] *virtio performance path* (ADR-0018) — an opt-in checkbox switches the Windows VM to a virtio-blk
        disk + virtio-net NIC and auto-downloads/caches the pinned virtio-win driver ISO (bring-your-own
        override), injecting the storage driver in windowsPE + storage/network in offlineServicing so Setup
        sees the virtio disk. SATA stays the no-download default. Unit-tested; live-smoked (QEMU accepts the
        virtio devices + Setup boots) — a complete virtio install needs an Ent/Edu/LTSC ISO (ConX wall).
- [x] qcow2 internal snapshots (create / list / revert / delete). `SnapshotService` wraps `qemu-img
      snapshot` per disk; `VmSnapshotService` orchestrates it across **all** of a VM's qcow2 disks so a
      multi-disk VM snapshots/reverts consistently (create is all-or-nothing with rollback; restore
      validates the tag is on every disk first; list shows only complete snapshots) — the cold analogue of
      the live/external path (ADR-0021). Stopped-only (exclusive image access). Surfaced in the CLI
      (`boxwright snapshot list|create|restore|delete`) and the GUI's VM detail panel.
- [ ] First public launch posts: r/qemu, r/linux, r/homelab, r/selfhosted,
      Hacker News — *only* once signed binaries exist for all three OSes.

**Success metrics (12 months from first public release):**
1,000 GitHub stars · 50 issues filed · 10 external contributors.

---

## Stage 3 — Power users (≈ months 7–12, conditional)

Invest here only if Stage 2 metrics trend positive.

- [x] Embedded **VNC** display — renders a VNC guest in-app via MarcusW.VncClient (set a VM's
      display to VNC); SPICE still uses remote-viewer. See ADR-0012.
- [x] **Guest audio** — Intel HD Audio sound card; audio plays over SPICE (remote-viewer), so there's
      no host-driver dependency. Host-direct playback for VNC/headless (pipewire/pa/coreaudio/dsound)
      is a fast-follow.
- [x] **Reconnect on restart** — a per-VM `runtime.json` lets the app re-adopt QEMU processes that
      survived an abnormal exit (reconnect QMP, show Running) instead of orphaning them. No daemon.
      See ADR-0014.
- [x] USB passthrough (ADR-0023). Devices pass through by **vendor:product** (stable across replug):
      `VmConfig.UsbDevices` → `CommandLineBuilder` emits `-device usb-host`, so a configured VM passes
      the device through from either front end. Host enumeration is capability-gated
      (`IUsbDeviceEnumerator`) across all three OSes — Linux **sysfs**, macOS **system_profiler**,
      Windows **SetupAPI** (parsers unit-tested; the macOS/Windows platform calls + GUI picker not yet
      smoke-tested on those OSes). CLI: `boxwright usb list|show|add|remove` with `--now` for live
      hot-plug/unplug (QMP `device_add`/`device_del`); GUI: a picker in VM Settings. Possible follow-up:
      ship UsbDk on Windows for driverless capture.
- [x] Bridged/TAP networking on Linux (ADR-0024). `NetworkConfig.Mode` ∈ `user`/`bridge`/`tap`;
      `CommandLineBuilder` emits `-netdev bridge,br=…` (via `qemu-bridge-helper`) or
      `-netdev tap,ifname=…,script=no` accordingly. Capability-gated: `NetworkValidation` fails a launch
      on a non-Linux host with a clear message. CLI: `boxwright net show|set <vm> <user|bridge|tap>`.
      Host setup (the bridge / TAP / setuid helper) is the user's responsibility — Boxwright never runs
      as root or reconfigures host networking. Command-line + gate unit-tested; not e2e'd against a live
      bridge here. GUI network editing is a possible follow-up.
- [x] Live performance graphs (ADR-0019) — CPU / RAM / disk sparklines in the VM detail view, polled
      ~1 s while running. CPU + RAM come from the QEMU host process; disk from QMP `query-blockstats`.
      Hand-drawn (no charting dependency). Network throughput is a fast-follow.
- [x] External/live snapshots via `blockdev-snapshot-sync` (+ `transaction`) — take a point-in-time of a
      **running** VM with no downtime, atomically across disks; revert/delete while stopped (qcow2 overlay
      chain, safe-mode `qemu-img rebase` on delete). See ADR-0021.

---

## v1.0 — The complete tool

- [ ] Embedded **SPICE** (clipboard, folder sharing, multi-monitor, USB redirect).
      (Evaluated 2026-06 and deferred — no .NET SPICE client, native FFI risks cross-platform
      parity, and remote-viewer is already smooth. See ADR-0013.)
- [~] VM templates + linked clones (ADR-0025). Linked/full clones exist (`VmCloneService`,
      `clone --linked`); per-VM MAC stamping (no bridge collisions) and templates are now built in Core +
      CLI: `VmConfig.IsTemplate`, launch-refusal for templates, and `boxwright template
      list|create|new|delete` (instances are linked clones by default, each a fresh non-template VM with
      its own MAC). Remaining: a GUI templates picker (phase 2) and a refuse-delete-when-instances-exist
      guard.
- [~] Headless mode / CLI parity (the GUI becomes optional, not mandatory). The `boxwright`
      CLI (`Boxwright.Cli`, ADR-0022) drives Core directly — `list`/`info`/`create` (blank or
      `--os <id>` from the catalog, with `--unattended`)/`clone`/`start` (with `--detach`)/`stop`/
      `display`/`delete`, `os list`, and offline `snapshot` (list/create/restore/delete); `--json`
      on the read commands. Catalog create runs the GUI's New-VM orchestration, now lifted into Core
      (`ICatalogVmInstaller`) and shared by both front ends — the GUI's `CatalogViewModel` delegates to
      it rather than duplicating the sequence. The CLI shares the per-VM folders and `runtime.json` with
      the GUI, so they interoperate. Remaining gap: Windows unattended stays GUI-only.
- [~] Plugin/recipe API for community-contributed OS definitions (ADR-0026). **Declarative JSON recipes**
      (data, not code; code plugins rejected for security / GPL / cross-platform / scope). Phase 1 built:
      `LocalRecipeCatalogSource` loads `recipes/*.json` (catalog documents) from a local folder and
      `CompositeOsCatalogSource` layers them over remote → cache → bundled (local wins by id); CLI
      `boxwright recipe dir|list|validate`; recipes surface in `os list` + the GUI picker. A recipe can now
      also carry a declarative **unattended** block (`UnattendedRecipe` + `RecipeInstaller`) in two kinds:
      `initrd-inject` (copy the kernel/initrd, inject a templated preseed/kickstart into the initrd, boot a
      templated cmdline — Debian/Fedora-style) and `cloud-init` (write the templated user-data as a NoCloud
      CIDATA seed disk, leaving the initrd untouched — Ubuntu-autoinstall-style). So the community can add a
      distro's hands-free install with no C#. Remaining (optional): re-express the four built-in per-family
      installers as recipes to prove full coverage.

---

## Decision gates (the honest part)

Evaluate at the **12-month** mark after first public release:

| GitHub stars | Verdict | Action |
|--------------|---------|--------|
| **< 500**    | Niche / personal | Keep as a learning + personal-use project. Stop marketing spend. Maintain `Boxwright.Qmp` on NuGet (it has standalone value). |
| **500–3,000**| Real traction | Invest in Stage 3 (embedded display, USB, snapshots). |
| **> 3,000**  | Community project | Set up governance, GitHub Sponsors, and seriously plan v1.0. |

**External signals that should trigger a re-plan at any time:**
- Quickgui resumes active development *and* adds Windows → the wedge narrows;
  reposition around UX/parity.
- Microsoft ships a friendly full-VM GUI for Windows → Windows wedge weakens.
- A well-funded competitor appears (watch r/virtualization monthly) → reassess
  differentiation.

---

## Realistic expectations

- **Floor:** ~500–2,000 stars within 18 months given a working Windows MVP.
- **Mid:** ~3k–8k stars over 2–3 years (between Quickgui's ~1.3k and Quickemu's
  ~14.6k).
- **Ceiling:** ~15k+ if it becomes the de-facto "VirtualBox without Oracle, VM
  without the Broadcom portal" on Windows. UTM's ~34k is an upper bound that
  benefits from being the *only* serious option on macOS/iOS — Boxwright will be
  compared on three fronts and will not monopolize any one of them.

The win is **breadth + UX + Quickemu-style ease**, not raw performance depth on
any single OS. On Windows VMware wins on speed; on Linux virt-manager wins on
power features; on macOS UTM wins on native polish. Boxwright wins by being the
same easy, open, daemon-free tool everywhere.

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
      behind an interface (remote manifest later). First cut: Ubuntu 24.04
      Desktop/Server, Debian 13 netinst; Fedora/Mint/Windows to follow (Windows
      needs a non-direct acquisition flow). See ADR-0010.
- [x] Checksum (SHA-256) verification + provenance display for downloads.
      GPG/PGP *signature* verification is a fast-follow.
- [ ] **Auto-attach virtio-win ISO** when creating a Windows guest.
- [ ] qcow2 internal snapshots (create / list / revert / delete).
- [ ] First public launch posts: r/qemu, r/linux, r/homelab, r/selfhosted,
      Hacker News — *only* once signed binaries exist for all three OSes.

**Success metrics (12 months from first public release):**
1,000 GitHub stars · 50 issues filed · 10 external contributors.

---

## Stage 3 — Power users (≈ months 7–12, conditional)

Invest here only if Stage 2 metrics trend positive.

- [ ] Embedded **VNC** display (drop the external remote-viewer dependency).
- [ ] USB passthrough wizard (ship UsbDk on Windows).
- [ ] Bridged/TAP networking on Linux.
- [ ] Live performance graphs from QMP `query-stats`.
- [ ] External/live snapshots via `blockdev-snapshot-sync` (+ `transaction`).

---

## v1.0 — The complete tool

- [ ] Embedded **SPICE** (clipboard, folder sharing, multi-monitor, USB redirect).
- [ ] VM templates + linked clones.
- [ ] Headless mode / CLI parity (the GUI becomes optional, not mandatory).
- [ ] Plugin/recipe API for community-contributed OS definitions.

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

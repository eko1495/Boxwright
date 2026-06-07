# ADR-0021: External / live VM snapshots (snapshot a running VM)

- **Status:** Accepted
- **Date:** 2026-06-07

## Context
Boxwright's internal snapshots (`SnapshotService` → `qemu-img snapshot`) require the VM to be **stopped** —
qcow2 internal snapshots need exclusive access to the image. A VirtualBox-style tool is also expected to
**snapshot a running VM with no downtime**. The roadmap's Stage-3 item "External/live snapshots via
`blockdev-snapshot-sync` (+ `transaction`)" delivers that. It is a pure QMP/`qemu-img` feature (no new
dependency, no daemon, cross-platform) and is accelerator-independent, so it works the same on WHPX/KVM/HVF.

`blockdev-snapshot-sync` freezes the disk's current image (it becomes a **read-only backing**) and continues
writing into a **new overlay**, building a qcow2 backing chain. So a "snapshot" is the frozen image at the
moment it was taken; the live disk is always the top overlay. This single fact drives the design.

## Decision
- **Take (live, while running).** `RunningVm.TakeLiveSnapshotAsync` resolves each qcow2 disk's live block
  **`device`** via QMP `query-block` (matched by inserted file path — the disks are launched
  `-drive ...,if=virtio` with no id/node-name, so the auto node-name is unstable) and issues **one
  `transaction`** of `blockdev-snapshot-sync` actions, so all disks are snapshotted **atomically**. After it
  succeeds the guest writes into the new overlays.
- **Repoint the config (mandatory).** A successful take immediately repoints `vm.json`'s `DiskConfig.File` to
  the new overlays. Without this, the next cold boot would mount the frozen base read-write — losing
  post-snapshot writes **and** corrupting the snapshot. vm.json is written before the sidecar.
- **A names/timestamps sidecar.** `live-snapshots.json` (beside `vm.json`/`runtime.json`) records each
  snapshot's id, name, timestamp, and per-disk frozen file. It is authoritative **only** for those
  user-facing facts; all **structural** decisions (revert/delete) read the actual qcow2 backing pointers via
  `qemu-img info`, never the sidecar (which can drift).
- **Revert (stopped).** For each disk, layer a **fresh overlay over the chosen snapshot's frozen file**
  (`IDiskService.CreateOverlayAsync`) and repoint the config to it. **Invariant: a frozen file is read-only
  and is never booted read-write** — revert always creates a new overlay, never points the disk at a frozen
  file. Non-destructive: other snapshots are untouched and the pre-revert overlay is left in place.
- **Delete (stopped).** Detach the snapshot's frozen file from the chain with safe-mode
  `qemu-img rebase -b <parent> <child>` (content-preserving) for **every** dependent image, then delete the
  frozen file **last** (so a mid-operation failure never leaves a dependent pointing at a removed backing).
  A new `IDiskService.RebaseAsync` is the seam; `-u` (unsafe) is never used.
- **Run-state gating + lean UI.** Take is enabled only while **Running**; revert/delete only while
  **Stopped** (mirroring internal snapshots — offline qcow2 access), each with a two-step confirmation. A
  "Live snapshots" card in the VM detail view, no charting/extra dependency.

## Consequences
- **Easier:** snapshot a running VM with zero downtime, atomically across disks; a full lifecycle
  (take/list/revert/delete) with real space reclamation on delete.
- **Harder / accepted:** snapshots form a qcow2 backing **tree** (revert branches it); the
  matched-by-file device resolution depends on `query-block` exposing the inserted file (verified live).
  **Deferred:** the "(current ancestor)" marker in the list; auto-cleanup of orphaned overlays left by a
  revert; deleting the **base** image (a snapshot whose frozen file has no parent) — it would need a
  flatten/compact pass, so it is refused with a clear message; and a "compact/merge all" action. Revert and
  delete share the same stopped-only exposure as internal snapshots (a cross-process running instance is the
  same pre-existing concern).

## Verification
- **Unit:** Qmp (`FakeQmpServer`) — `query-block` parsing (device/file/driver), the `transaction` payload
  shape (N actions, `device` not node-name, absolute `snapshot-file`, `mode:absolute-paths`), empty-actions
  guard. Core — `LiveSnapshotService` Take repoints every disk + records the frozen file; Revert layers an
  overlay over the frozen file and **never** points at it; Delete rebases **every** child onto the parent
  then deletes the frozen file last, aborts-before-delete on a rebase failure, and refuses a base image with
  dependents; `DiskService.RebaseAsync` uses safe mode (never `-u`); `DiskInfo` parses backing filenames;
  manifest round-trip/missing/bad-schema. App — Take gated to Running, revert/delete gated to Stopped, with
  two-step confirms. Full suite green (Qmp 54 · Core 144 · App 94), `dotnet format` clean, 0 warnings.
- **Live (real QEMU/WHPX):** booted a VM, took a live snapshot while running (the transaction succeeded, the
  overlay appeared, `qemu-img info --backing-chain` showed base ← overlay, and vm.json repointed); wrote a
  marker in the guest, took a second snapshot, stopped, **reverted** to the first (marker gone), and
  **deleted** an intermediate snapshot (the live disk still `qemu-img check`ed clean and booted).

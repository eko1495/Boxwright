# ADR-0028: VM rename — human-readable folder slug while the id stays the stable key

- **Status:** Accepted (phase 1 — Core + CLI; GUI rename deferred)
- **Date:** 2026-06-16

## Context
Every VM lives in `root/<id>/` where `<id>` is a GUID (ADR-0006). That makes the VMs folder
un-browsable: you can't tell `9f3c…` from `a1b2…` without opening each `vm.json`. Users want a
readable folder (`ubuntu-dev-…`), the way a settings panel "rename" feels. But two facts make a naïve
folder rename dangerous:

1. **The id is load-bearing.** `VmRepository` keys everything off the id, and — crucially — **linked
   clones embed an *absolute* qcow2 backing path into the source VM's folder** (`VmCloneService`).
   Renaming a folder that backs a clone points the clone at a path that no longer exists and corrupts it
   irreparably. The id, not the folder name, is what nothing may break.
2. **`SaveAsync(VmConfig)` recomputed the folder from the id** (`folder = root/<id>`). So today
   `folder == id` is an *invariant enforced on every write*, not just a convention: the very next
   `boxwright set <vm> --memory …` would write `vm.json` into a freshly-created `root/<id>` folder and
   **orphan** a renamed slug folder (with all its disks and `runtime.json`). The read path
   (`ListAsync`) already loads `new Vm(folder, config)` from the *actual* folder, so only the write path
   assumed `folder == id`.

## Decision
- **The id remains the immutable internal key** inside `vm.json`; linked-clone backing chains and every
  repository lookup continue to key off it. **The folder name becomes cosmetic.**
- **The folder name becomes a slug:** a kebab-case, cross-platform-safe sanitization of the display name
  plus a short id suffix — e.g. `ubuntu-dev-1a2b3c4d`. The id suffix guarantees uniqueness even when two
  VMs share a name and keeps the folder traceable to its VM.
- **id-only folders stay valid forever.** Migration is opt-in / lazy: `VmRepository` never assumes
  `folder == id` on read, so a never-renamed VM keeps its GUID folder with no penalty.
- **`SaveAsync` and `DeleteAsync` resolve a VM's folder by its id, not `root/<id>`.** `VmRepository`
  gains a private `FindFolderByIdAsync` (an O(1) `root/<id>` fast path, scanning only when a VM has been
  renamed). `SaveAsync(VmConfig)` and `DeleteAsync(id)` both use it, so **every** edit path (the GUI
  settings save, `set`/`net`/`usb`/`template`, live-snapshot config rewrites, the catalog installer) and
  delete-by-id stay in the slug folder automatically — no per-call-site change and no orphaning. A
  `SaveAsync(Vm)` overload also exists as an explicit-folder fast path for callers already holding the
  `Vm` (it skips the lookup). New VMs (`CreateAsync`, before the folder exists) fall back to `root/<id>`.
- **A guarded `IVmRenameService` / `VmRenameService`** does the rename. It:
  1. **Refuses when the VM backs a linked clone**, reusing `IVmDeletionService.FindDependentsAsync`
     (the same qcow2-backing scan the delete-guard uses — not re-derived) so the absolute-backing-path
     hazard above is impossible.
  2. **Refuses when the VM has live runtime state** (an open file would make `Directory.Move` fail or
     half-complete).
  3. Computes a collision-free slug (sanitize Windows-invalid characters and reserved device names →
     kebab → cap length → append id suffix → numeric-dedupe against existing folders, case-insensitively
     on Windows), writes the new name into the current folder, then `Directory.Move`s the folder
     (atomic, same-volume) and returns the relocated `Vm`.
- **CLI surface:** `boxwright rename <id|name> <new-name>` updates the display name **and** reslugs the
  folder. `boxwright set --name` keeps its existing display-name-only behaviour (no folder churn) and
  points users to `rename`.

## Running-detection asymmetry (honest limit)
Core can only see whether `runtime.json` is **present**; it can't PID-verify that the recorded QEMU
process is actually alive (that check lives in the CLI's internal `VmStatusProbe`, via
`IProcessLauncher.Attach`). So **the CLI command does the authoritative liveness guard** before calling
the service, and the service's `runtime.json`-presence check is a secondary belt-and-braces guard. A
future GUI consumer should likewise gate on its own running check, not rely on Core alone.

## Consequences
- **Easier:** the VMs folder is finally browsable; a copied/moved slug folder still works because the id
  inside `vm.json` is what matters.
- **Harder / accepted:** a VM that backs a linked clone can't be renamed until the clone is removed or
  made full (the same constraint as deleting it). Renaming is stopped-only.
- **GUI rename deferred** to a phase-2 follow-up. The logic lives in Core (`IVmRenameService`) and is
  registered in both composition roots (ADR-0022), so a viewmodel can adopt it later with no Core change.

## Alternatives considered
- **Rename the id itself.** Rejected: it would have to rewrite every linked clone's absolute backing
  path and any external reference — exactly the corruption this ADR avoids. The id is permanent.
- **Pure display-name rename, no folder change.** Rejected: the ask was a *browsable* folder; that's
  what `set --name` already does, and it doesn't solve the problem.
- **A copy+delete "move".** Rejected: slower, and non-atomic across a crash. `Directory.Move` within the
  same root is atomic; cross-volume doesn't apply because the slug is a sibling of the old folder.

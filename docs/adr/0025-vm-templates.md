# ADR-0025: VM templates (clone instances from a frozen base)

- **Status:** Accepted (phase 1 — Core + CLI — implemented; GUI is phase 2)
- **Date:** 2026-06-15

## Context
The v1.0 roadmap line "VM templates + linked clones" is half-done: `VmCloneService` already does full and
linked (qcow2-overlay) clones, and the CLI exposes `boxwright clone … [--linked]`. What's missing is the
**template** concept: a VM you mark as a reusable base and spin *instances* off quickly, rather than ad-hoc
cloning. The pieces to build on already exist — `VmCloneService`, the ADR-0021 frozen-base + overlay
machinery (`IDiskService.CreateOverlayAsync`/`RebaseAsync`), and `VmRepository` (fresh GUID per VM).

Two real problems a naïve "just clone it" approach hits:
1. **Backing-image safety.** A linked clone is a qcow2 overlay over the source's disk; if the source is
   later booted and writes to that disk, every linked instance is corrupted. A template's disk must be
   **frozen read-only** once it's a template.
2. **Identity collisions.** `CommandLineBuilder` currently emits **no MAC address**, so every VM gets
   QEMU's default `52:54:00:12:34:56`. Behind user-mode NAT that's invisible, but two instances on a
   **bridge** (ADR-0024, just shipped) collide. Instances need a unique MAC — a latent bug that templates
   force us to fix.

## Decision (proposed)
- **`VmConfig.IsTemplate` (bool, default false).** A template is **not bootable** — `VmLauncher`/the UI
  refuse to start it with a clear message ("this is a template; create an instance from it"), because its
  disk is the frozen backing for linked instances.
- **Templatize:** convert a *stopped* VM into a template — set `IsTemplate`, and **freeze its disk**
  read-only (the ADR-0021 mechanism: the active image becomes a read-only base). Done once.
- **Instantiate:** create a new VM from a template via `VmCloneService.CloneAsync` (linked by default for
  instant/space-efficient instances; `--full` for an independent copy), with a fresh GUID + name and a
  **freshly generated MAC** (and a per-instance hostname seed where applicable).
- **Per-VM MAC (prerequisite sub-feature):** add `NetworkConfig.MacAddress` (empty = let QEMU assign, the
  current behavior). Generate a locally-administered unicast MAC (`52:54:00:xx:xx:xx`) on **create and
  clone** so instances never collide; `CommandLineBuilder` emits `…,mac=<addr>` on the NIC when set. This
  lands as part of templates but benefits ordinary clones too.
- **Surface:** a Core `ITemplateService` (thin: freeze + clone + identity reset) and a CLI
  `boxwright template list|create|new|delete`. GUI ("Templates" group in the list, "New from template"
  action) is a phase-2 follow-up.

## Phasing
1. **Core + CLI — DONE.** Per-VM MAC (shipped first, ADR-0025 prerequisite); `VmConfig.IsTemplate`;
   `VmLauncher` refuses to boot a template; `VmCloneService` resets `IsTemplate`/MAC so an instance is a
   fresh concrete VM; `boxwright template list|create|new|delete`. The disk "freeze" is achieved by the
   launch-refusal (a template never boots, so its disk — the linked-instance backing — never changes), so
   no separate `chmod`/convert step was needed.
2. **GUI (pending):** templates surfaced/grouped in the VM list; a "New from template" dialog. (Avalonia —
   wants a real GUI smoke test, per the pattern.)
3. **Deferred:** refuse-delete-when-linked-instances-exist (needs a backing-chain scan across VMs) — for
   now `template delete` warns and requires `--yes`, the same exposure as deleting any linked-clone source.

## Consequences
- **Easier:** stamp out many instances from a known-good base in seconds (linked); the MAC fix also cures
  a real bridged-networking collision for all clones.
- **Harder / accepted:** a template's disk is frozen, so *updating* a template means cloning it, editing
  the clone, and re-templatizing (no in-place template edit while instances exist — same constraint as
  linked clones generally). Deleting a template that still has linked instances must be refused (its base
  is in use) — mirrors the live-snapshot delete rules.

## Alternatives considered
- **A separate templates registry/folder** instead of an `IsTemplate` flag. Rejected: it fights ADR-0006
  (one VM = one folder + JSON); a flag keeps templates discoverable by the same `VmRepository` scan.
- **Full copies only (no linked instances).** Rejected: slow and space-hungry; linked-over-frozen-base is
  the whole point. `--full` stays available for portability.

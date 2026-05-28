# ADR-0006: One VM = one folder + one JSON config; no daemon

- **Status:** Accepted
- **Date:** 2026-05-28

## Context
We need to decide how VM definitions and state are stored. Options range from a
central database/registry (and/or a managing daemon, as libvirt uses with domain
XML) to self-contained per-VM files. Quickemu users specifically praise the
"it's just a file" portability (copy the config and disk = move the VM, run from
an external drive).

## Decision
**Each VM is a single folder** containing one **human-readable JSON** config
(`schemaVersion`-stamped) plus its disk image(s) and logs. There is **no central
registry, no database, and no daemon.** Moving or backing up a VM is copying its
folder. `CommandLineBuilder` is the one place that translates the JSON config
into a QEMU command line. (Config shape: see `architecture.md` §9.)

The schema is versioned so format changes are explicit and migratable.

## Consequences
- **Easier:** trivial backup/restore/portability; transparent, diffable configs;
  no global state to corrupt; no service to install.
- **Easier:** power users can hand-edit or template configs; great for support
  ("attach your VM's JSON").
- **Harder:** features that a central store would make simple (global storage
  pools, shared networks) must be modeled per-VM or as separate explicit
  concepts. Acceptable given our desktop scope.
- **Discipline:** VM state must live only in the per-VM folder + its config —
  nothing hidden in app-global locations (enforced as an anti-pattern in
  `CLAUDE.md`).

## Alternatives considered
- **Central SQLite/registry + daemon:** rejected — installation friction,
  privilege needs, opacity, and conflict with ADR-0001/0003.
- **libvirt domain XML:** rejected with libvirt itself (ADR-0001).
- **A bespoke binary format:** rejected — opaque and un-diffable; JSON is
  inspectable and trivially handled by `System.Text.Json`.

# ADR-0007: Bundle QEMU per platform; ship QMP as a standalone library

- **Status:** Accepted
- **Date:** 2026-05-28

## Context
Two distribution/structure questions: (1) Do we require users to install QEMU
themselves, or bundle it? (2) Is the QMP client an internal detail or a
first-class, separately useful artifact? And should the repo be a monorepo or
split?

User-installed QEMU is a major friction point (especially on Windows, where
"get a QEMU build" is itself a hurdle). A clean C# QMP client does not exist on
NuGet, so there is standalone demand for one.

## Decision
**Bundle QEMU per platform**, UTM-style:
- Windows installer ships `qemu-system-*` + `qemu-img`.
- macOS ships a **signed** QEMU inside the `.app` (with the hypervisor
  entitlement for HVF).
- Linux bundles via Flatpak or uses the system QEMU.
Bundled QEMU is unmodified and accompanied by its source/offer per ADR-0005.

**Ship `Boxwright.Qmp` as a standalone, GUI-agnostic NuGet package**, published
as soon as it is usable. It must not depend on Avalonia or `Boxwright.Core`.

**Structure: monorepo for now** — GUI, `Core`, `Qmp`, tests, and docs in one
repository. `Boxwright.Qmp` is published from the monorepo. If it later
stabilizes and gains independent traction, extracting it to its own repo is a
future decision (a new ADR).

## Consequences
- **Easier (users):** zero-install VM experience; the biggest Windows friction
  disappears.
- **Easier (community):** the QMP package is a second discoverable artifact that
  can attract its own users and contributors and fills a real ecosystem gap.
- **Easier (dev):** monorepo keeps cross-cutting changes and docs atomic.
- **Harder:** installer size grows ~80–200 MB; release process must keep the
  bundled QEMU's source/offer attached and the macOS build signed/notarized.
- **Harder:** publishing a library from a monorepo needs care to keep its
  dependency surface clean (no leakage from `Core`/`App`).

## Alternatives considered
- **Require user-installed QEMU:** simplest to build, worst UX — exactly the
  friction we exist to remove. (Dev builds may still use system QEMU.)
- **Keep QMP internal:** rejected — forfeits a free community/ecosystem win.
- **Split repos from day one:** rejected for now — premature; slows early
  cross-cutting iteration. Revisit once `Qmp` is stable.

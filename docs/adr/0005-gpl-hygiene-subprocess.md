# ADR-0005: Keep QEMU at arm's length as a subprocess (GPL hygiene)

- **Status:** Accepted
- **Date:** 2026-05-28

## Context
QEMU is licensed GPLv2 (with some components GPLv2-or-later). We want Boxwright's
own code under a permissive license (**MIT**) so it is maximally reusable and
contributor-friendly, and so the `Boxwright.Qmp` library is freely adoptable.
We must avoid creating a derivative work of QEMU that would impose GPL terms on
our code.

## Decision
QEMU is treated as an **external program invoked as a separate process**. We:
- launch `qemu-system-<arch>` and `qemu-img` as child processes;
- communicate over QMP (a socket protocol) and command-line arguments;
- **never** statically link QEMU, **never** P/Invoke into QEMU libraries, and
  **never** vendor or modify QEMU source in this repository.

When we distribute bundled QEMU binaries (ADR-0007), we ship them **unmodified**
and provide the corresponding source (or a written offer plus the upstream
link), satisfying GPL distribution obligations. Boxwright's source stays MIT.
This mirrors UTM's separation (permissive frontend, GPL components kept at
arm's length).

## Consequences
- **Easier:** clean MIT licensing for all Boxwright code; no GPL contagion;
  `Boxwright.Qmp` is freely adoptable from NuGet.
- **Obligation:** when bundling QEMU we must accompany it with its source/offer
  and keep it unmodified. A release checklist enforces this.
- **Constraint on contributors:** PRs must not introduce linking against or
  vendoring of QEMU code. Communication stays over process boundaries.

## Alternatives considered
- **License Boxwright under GPL too:** rejected — reduces reuse of the QMP
  library and contributor reach; unnecessary given clean process separation.
- **Statically link QEMU for a single binary:** rejected — creates a derivative
  work and GPL obligations on our code; also harms cross-platform packaging.

> This ADR is engineering guidance for keeping the licenses clean, not legal
> advice. For a commercial release, confirm with a lawyer.

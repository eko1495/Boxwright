# ADR-0022: Headless command-line interface (`boxwright` CLI)

- **Status:** Accepted
- **Date:** 2026-06-14

## Context
The roadmap's v1.0 line item "Headless mode / CLI parity" calls for driving VMs
without the GUI — for scripting, CI, remote/SSH hosts, and homelab automation. The
architecture already makes this cheap: all orchestration (launch, accelerator
detection, disks, snapshots, runtime state, OS catalog) lives in **`Boxwright.Core`**,
which is UI-agnostic (Directive 4 / ADR-0002). The GUI is a thin Avalonia shell over
Core; a CLI is simply a *second* thin shell over the same Core.

The on-disk model makes the two shells interoperate for free: one VM is one folder
with `vm.json` + disks, and a running VM records `runtime.json` (ADR-0006, ADR-0014).
So a VM created in the GUI can be started from the CLI, and a VM started from the CLI
can be adopted by the GUI — no daemon, no shared in-memory state, no IPC. The CLI is
just another process that reads/writes the same folders and talks QMP to the same QEMU.

## Decision
- **A new project `src/Boxwright.Cli`** (`boxwright` executable) that depends only on
  `Boxwright.Core` (and the DI/logging packages already in the repo). It contains **no**
  QEMU/process/QMP logic of its own — that all stays in Core (Directive 8 / anti-pattern
  "no business logic in the shell"). The CLI is parsing + presentation + a composition
  root that mirrors the App's `ServiceConfiguration` **minus Avalonia**.
- **Commands (MVP):** `list`, `info`, `create`, `start`, `stop`, `display`, `delete`,
  `os list`, and `snapshot list|create|delete`. VMs are addressed by **id, exact name,
  or a unique id prefix** (`VmResolver`).
- **Start is foreground by default; `--detach` leaves it running.** Foreground start
  blocks (draining QEMU output to the per-VM log) until the guest exits or Ctrl+C, which
  triggers a graceful `StopAsync`. `--detach` returns immediately and relies on the
  persisted `runtime.json` so a later `stop`/`display`/`list` re-adopts the process
  (ADR-0014). Detach inherits the existing orphaned-stdout-pipe caveat of the reconnect
  model — acceptable because a booted guest is quiet on stderr; documented, not hidden
  (Directive 9).
- **`create` is deliberately minimal:** a blank VM with a freshly-`qemu-img`'d disk and
  an optional `--iso` installer attached. The one-click OS catalog download and
  unattended-install seed generation (ADR-0010/0013/0015…) stay GUI-only for now; the CLI
  exposes `os list` so scripts can see catalog ids, but ISO acquisition is the user's job.
- **Testable by construction:** commands take their Core collaborators via constructor
  injection and write to an injected pair of `TextWriter`s, so the parser, the resolver,
  the table renderer, and the read-only/disk commands are unit-tested with fakes and temp
  folders — no real QEMU (mirroring the rest of the repo's test philosophy).

## Consequences
- **Easier:** scripting and CI ("`boxwright start ci-runner --detach`"); managing VMs on
  a headless host; a second consumer that keeps Core honest about staying UI-free. The CLI
  and GUI share one source of truth on disk, so they compose without coordination.
- **Harder / accepted:** two front ends to keep at parity as Core grows (each new Core
  capability is a candidate CLI command). `create` is not yet at GUI parity (no catalog
  download, no unattended install) — a follow-up can lift the App's New-VM orchestration
  into Core and let both shells call it. Concurrent mutation of the *same* VM from CLI and
  GUI is possible (both can issue power actions); QMP serializes the guest-side effect and
  `runtime.json` is best-effort, matching the existing ADR-0014 exposure.

## Alternatives considered
- **A `--headless` flag on the Avalonia app.** Rejected: it would drag the Avalonia
  dependency (and a display/render stack) onto headless/CI hosts for no benefit, and muddy
  the App's single-window lifetime. A separate executable keeps the dependency surface
  honest and the App focused.
- **A long-running CLI daemon managing VMs.** Rejected outright — Directive 6 / anti-pattern
  "no background daemon." The CLI is one-shot; persistence is the per-VM folder.
- **A third-party command-line framework (e.g. System.CommandLine).** Rejected for the MVP
  to keep dependencies minimal and parsing trivially testable; a hand-rolled
  `--flag` / `--key=value` parser covers the current surface. Revisit if the command tree
  grows complex.

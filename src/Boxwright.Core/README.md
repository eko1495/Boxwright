# Boxwright.Core

The orchestration / domain layer. Everything that knows what a "VM" is, but
nothing about the UI.

May depend on `Boxwright.Qmp`. **Must not** depend on Avalonia (enforced by
review — see [`CLAUDE.md` §4](../../CLAUDE.md)).

## Responsibilities

- `VmConfig` — the versioned JSON VM model (load/save via `System.Text.Json`).
- `CommandLineBuilder` — turns a `VmConfig` into a QEMU argument list. Pure and
  exhaustively unit-tested (the riskiest correctness surface).
- `AcceleratorDetector` — auto-selects `kvm` / `hvf` / `whpx` / `tcg`. Never
  hardcodes an accelerator. (See [ADR-0003](../../docs/adr/0003-process-per-vm.md).)
- `QemuProcess` — spawns and supervises one `qemu-system-<arch>` child process
  per VM, capturing logs.
- `DiskService` — wraps the `qemu-img` subprocess (qcow2 default).
- `DisplayLauncher` — launches `remote-viewer` for the MVP display.
  (See [ADR-0004](../../docs/adr/0004-display-via-remote-viewer.md).)

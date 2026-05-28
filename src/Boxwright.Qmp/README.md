# Boxwright.Qmp

A standalone, cross-platform C# client for the **QEMU Machine Protocol (QMP)**.

This library is the protocol layer of [Boxwright](../../README.md) but is
designed to stand on its own and is published to NuGet as `Boxwright.Qmp`.

**Hard rule:** this project references **nothing** else in the repo and **no
UI framework**. It depends only on the .NET BCL (`System.Text.Json`, sockets).
Keep it that way — see [ADR-0007](../../docs/adr/0007-bundled-qemu-and-qmp-library.md).

## Scope

- Connect to a QMP endpoint (TCP or Unix socket) and run the `qmp_capabilities`
  handshake.
- Send `execute` commands with correlated, awaitable replies.
- Surface asynchronous QMP events (`SHUTDOWN`, `RESET`, `STOP`, …) as a stream.
- Probe `query-qmp-schema` for capability detection.

No retry/policy logic lives here — that belongs to `Boxwright.Core`.

See the interface sketch in [`docs/architecture.md` §4](../../docs/architecture.md).

## Testing

Driven against an in-memory/loopback fake QMP server — **no live QEMU required**.
See `tests/Boxwright.Qmp.Tests`.

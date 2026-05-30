# Boxwright.E2E — manual end-to-end harness

A small console app that exercises the **real** `Boxwright.Core` VM lifecycle against a
**real QEMU install** — the integration the fake-based unit tests can't cover:

```
detect accelerator → create config + qcow2 disk → launch qemu-system-x86_64
→ connect QMP → pause / resume / reset → graceful + force stop
```

It is **not** run by `dotnet test`. Run it on demand:

```
dotnet run --project tools/Boxwright.E2E
```

**Requirements:** `qemu-system-x86_64` and `qemu-img` on `PATH` (or, on Windows, the
default `C:\Program Files\qemu`). If QEMU isn't found, the harness **skips** (exit 0).
A QEMU window may appear briefly during the run.

**Exit code:** `0` = all checks passed (or skipped because QEMU is absent); `1` = a
failure — the guest's `qemu.log` is printed to help diagnose.

> This harness caught the WHPX `-cpu host` incompatibility on Windows (fixed in
> `CommandLineBuilder`). Re-run it after changing the launch path (`CORE-5`/`CORE-7`/
> `CORE-8`/`CORE-9`) to confirm real QEMU still boots.

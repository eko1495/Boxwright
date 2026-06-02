# Development Backlog — Stage 0 & Stage 1 (MVP)

This backlog covers **only Stage 0 (validation) and Stage 1 (MVP)** from
[`roadmap.md`](roadmap.md). Stage 2 and beyond are deliberately **not** planned
here — they are revised after the go/no-go gates, so detailed planning now would
be wasted (see [Out of scope](#out-of-scope-stage-2)).

It reflects the decisions already made in [`CLAUDE.md`](../CLAUDE.md),
[`architecture.md`](architecture.md), and the [ADRs](adr/). Every item respects
the Prime Directives (CLAUDE.md §2) and the layering rules (§4): subprocess-only
QEMU control via QMP, no libvirt, accelerator auto-detected (never hardcoded), no
QEMU/process/QMP logic in `App`, and `Boxwright.Qmp` kept GUI-agnostic and
NuGet-shippable.

> **Planning only.** No implementation code is written by this document.

---

## How to read this

Each item has a stable ID (`QMP-3`, `CORE-5`, …) used by **Depends on**. Build
order follows the dependency direction **Qmp → Core → App**, foundational work
first. See [Suggested critical path](#suggested-critical-path) for sequencing.

**Size** (solo developer, weekend cadence):

| Size | Rough effort |
|------|--------------|
| **S** | ≤ one evening |
| **M** | one to two weekends |
| **L** | several weekends |

**Labels:** `spike` `qmp` `core` `app` `accel` `disk` `display` `build`
`packaging` `test` `nuget` `license` `gate`.

**⚠ flags** mark a meaningful technical risk or an open question **not yet
settled by an ADR**. These are the items most likely to slip or force a small
decision. They are rolled up in [Open questions & risks](#open-questions--risks).

---

# Stage 0 — Validate the foundation

> ## 🚦 GATE-0 — Go / No-Go checkpoint
>
> **Everything in Stage 1 below is conditional on passing this gate.** Do not
> scaffold the real solution or write production code until the QMP round-trip
> is proven to feel clean in C#.
>
> **Gate question (from the roadmap):** *Does the QMP round-trip feel clean in
> C# within ~2 hours, on both Linux (KVM) and Windows (WHPX)?*
>
> - **Pass →** proceed to Stage 1, starting with `BLD-1`.
> - **Fail →** reconsider the stack (language/library choices) before going
>   further. This is a real decision point, not a formality.
>
> The Stage 0 work is a **throwaway spike** — a scratch console app, *not* part
> of the real solution. Hardcoding `-accel kvm`/`-accel whpx` here is fine
> because it is throwaway validation; the product never hardcodes the
> accelerator (that is `CORE-4`, per Directive 5 / ADR-0003).

### S0-1 — QMP round-trip spike on Linux (KVM)
- **Project:** cross-cutting (throwaway, outside `src/`)
- **Size:** S
- **Labels:** `spike` `qmp` `accel`
- **Depends on:** —
- **Description:** From a scratch C# console app on Linux, launch
  `qemu-system-x86_64 -accel kvm -m 2048 -qmp tcp:127.0.0.1:4444,server,nowait …`,
  connect a `TcpClient`, send `{"execute":"qmp_capabilities"}` then
  `{"execute":"query-status"}`, and parse the reply with `System.Text.Json`.
- **Acceptance criteria:**
  - [ ] QEMU launches with a TCP QMP endpoint and the console app connects.
  - [ ] Capabilities handshake succeeds; `query-status` returns a parsed status
        (e.g. `running`/`prelaunch`) read out of the JSON.
  - [ ] A subjective verdict is recorded: did this feel ergonomic in C# within
        ~2 hours? (Feeds GATE-0.)

### S0-2 — QMP round-trip + WHPX confirmation on Windows 11
- **Project:** cross-cutting (throwaway, outside `src/`)
- **Size:** S
- **Labels:** `spike` `qmp` `accel`
- **Depends on:** S0-1
- **Description:** Repeat S0-1 on Windows 11 with a
  [qemu.weilnetz.de](https://qemu.weilnetz.de) build using `-accel whpx`, and
  confirm WHPX is actually usable on the target machine. Windows is the
  strategic wedge, so its viability must be proven first.
- **Acceptance criteria:**
  - [ ] QEMU (weilnetz build) launches with `-accel whpx` and does **not** fall
        back to TCG on the dev machine.
  - [ ] TCP QMP handshake + `query-status` round-trip works identically to S0-1.
  - [ ] Any WHPX caveats hit (e.g. needing `kernel-irqchip=off`, conflict with a
        co-installed VirtualBox/Hyper-V) are written down for `CORE-4`/`PKG-1`.
- **⚠ Risk / open question:** WHPX may be disabled, unavailable, or conflict with
  other hypervisors on the target machine (architecture §5). If WHPX is not
  usable, the Windows wedge is in question — surface this *before* investing in
  Stage 1.

---

# Stage 1 — MVP *(conditional on passing GATE-0)*

The smallest version a stranger can use. **Windows must work on day one.**
Ordered: build foundation → `Qmp` → `Core` → `App` → packaging → exit gate.

## Foundation / build (cross-cutting)

### BLD-1 — Create the solution and a green build/test baseline
- **Project:** cross-cutting/build
- **Size:** S
- **Labels:** `build` `test`
- **Depends on:** GATE-0
- **Description:** Create `Boxwright.sln`, add the five existing stub projects,
  add the missing test packages (`Microsoft.NET.Test.Sdk`, `xunit`,
  `xunit.runner.visualstudio`, `coverlet.collector`) to both test projects, and
  confirm a clean build + test run. Settle the **target framework** question
  while here (the props file pins `net8.0`; CLAUDE.md notes .NET 10 is the
  current LTS).
- **Acceptance criteria:**
  - [ ] `Boxwright.sln` contains all 5 projects (`scripts`/CI use it).
  - [ ] `dotnet build` is clean with **no new warnings** (Release treats
        warnings as errors per `Directory.Build.props`).
  - [ ] `dotnet test` runs and passes with at least one trivial test per test
        project.
  - [ ] `dotnet format` produces no diff.
  - [ ] The chosen `TargetFramework` is confirmed/pinned in
        `Directory.Build.props` with a one-line rationale.
- **⚠ Risk / open question:** TFM choice (stay on the `net8.0` floor vs. bump to
  `net10.0` LTS) is unsettled by any ADR. Bumping later is cheap, but Avalonia
  version compatibility (`APP-1`) should agree with the choice.

### BLD-2 — CI pipeline: build + test + format on all three desktop OSes
- **Project:** cross-cutting/build
- **Size:** M
- **Labels:** `build` `test`
- **Depends on:** BLD-1
- **Description:** GitHub Actions workflow that runs `dotnet build`,
  `dotnet test`, and `dotnet format --verify-no-changes` on Windows, Linux, and
  macOS for every push/PR. (The full 5-target *release/packaging* matrix incl.
  arm64 is deferred to the `PKG-*` items; this is the developer feedback loop.)
- **Acceptance criteria:**
  - [ ] Workflow runs on `windows-latest`, `ubuntu-latest`, `macos-latest`.
  - [ ] A formatting violation or a failing test fails the check.
  - [ ] Status is visible on PRs.

### BLD-3 — Architecture/layering guard test
- **Project:** cross-cutting/build (`tests`)
- **Size:** S
- **Labels:** `build` `test`
- **Depends on:** BLD-1
- **Description:** An automated test that enforces the dependency direction and
  the anti-patterns in CLAUDE.md §4/§8: `Boxwright.Qmp` references nothing of
  ours and no Avalonia; `Boxwright.Core` does not reference Avalonia; `App`
  contains no `Process`/`qemu-img`/QMP calls. (Reflection over assembly
  references, or a tool like NetArchTest.)
- **Acceptance criteria:**
  - [ ] Test fails if `Boxwright.Qmp` gains a reference to Avalonia, `Core`, or
        `App`.
  - [ ] Test fails if `Boxwright.Core` references Avalonia.
  - [ ] Runs in CI (`BLD-2`).
- **⚠ Risk / open question:** Detecting "process/QMP logic leaked into `App`" is
  partly heuristic (reference-level checks are reliable; call-level checks are
  best-effort). Treat the reference checks as the hard gate.

---

## `Boxwright.Qmp` — protocol layer

> Must stay GUI-agnostic and independently NuGet-publishable (Directive 8,
> ADR-0002/0007). No Avalonia, no `Core`, no app concepts. Tested against a
> fake loopback socket — **never** a live QEMU (CLAUDE.md §6).

### QMP-1 — Wire protocol types and client surface
- **Project:** `Boxwright.Qmp`
- **Size:** S
- **Labels:** `qmp`
- **Depends on:** BLD-1
- **Description:** Define the public surface and DTOs from architecture §4.2:
  `IQmpClient`, `QmpEndpoint` (`Tcp`/`UnixSocket` factories), `QmpEvent`,
  `QmpCommandException` (with `ErrorClass`/`Description`), and the internal
  request/response envelope records for `System.Text.Json`.
- **Acceptance criteria:**
  - [ ] Public types compile and match the architecture §4.2 sketch (names may
        refine, shape may not regress).
  - [ ] `QmpEndpoint` supports both TCP (`127.0.0.1:port`) and Unix-socket paths.
  - [ ] Serialization of an `{"execute":…,"arguments":…,"id":…}` envelope is
        unit-tested round-trip with `System.Text.Json`.

### QMP-2 — Fake loopback QMP server test fixture
- **Project:** `Boxwright.Qmp.Tests`
- **Size:** M
- **Labels:** `qmp` `test`
- **Depends on:** QMP-1
- **Description:** A scriptable in-process fake QMP server (loopback TCP and/or
  pipe) that emits the greeting banner, accepts `qmp_capabilities`, returns
  canned `return`/`error` replies keyed by `id`, and can push unsolicited
  `event` messages on demand. This is the harness every other `QMP-*` test uses.
- **Acceptance criteria:**
  - [ ] Fixture sends a realistic `QMP` greeting banner on connect.
  - [ ] Can be programmed to reply to a given command with a `return` **or** an
        `error`, and to emit events at chosen times.
  - [ ] Usable from xUnit tests without any QEMU binary present.

### QMP-3 — Connect + capabilities handshake
- **Project:** `Boxwright.Qmp`
- **Size:** M
- **Labels:** `qmp`
- **Depends on:** QMP-1, QMP-2
- **Description:** Implement `ConnectAsync`: open the socket, read the greeting
  banner, send `qmp_capabilities`, and transition to connected. Honor the
  `CancellationToken`. No retry policy here (that belongs to `Core`).
- **Acceptance criteria:**
  - [ ] Against `QMP-2`, `ConnectAsync` completes and `IsConnected` is true after
        a successful handshake.
  - [ ] A malformed/absent greeting or a failed handshake throws a clear,
        documented exception.
  - [ ] Cancellation during connect is observed and surfaces `OperationCanceled`.

### QMP-4 — Correlated `execute` + background read loop
- **Project:** `Boxwright.Qmp`
- **Size:** M
- **Labels:** `qmp`
- **Depends on:** QMP-3
- **Description:** The core of the client. A single background read loop parses
  each incoming JSON line and dispatches: `return`/`error` replies matched to the
  awaiting caller **by `id`** (do not assume response ordering), events to the
  event stream (`QMP-5`). `ExecuteAsync(command, arguments, ct)` tags a unique
  `id`, sends, and awaits the correlated reply; `error` replies throw
  `QmpCommandException`.
- **Acceptance criteria:**
  - [ ] Two concurrent `ExecuteAsync` calls receive the correct replies even when
        the fake server returns them out of order.
  - [ ] An `error` reply throws `QmpCommandException` with `ErrorClass` +
        `Description` populated.
  - [ ] Events interleaved with replies do not drop or mis-route either.
  - [ ] Disposal/disconnect cancels in-flight awaiters deterministically.
- **⚠ Risk / open question:** This is the trickiest `Qmp` piece — concurrency
  around the read loop, id correlation, and graceful teardown are classic bug
  sources. Budget extra test time; the fake server (`QMP-2`) must exercise
  out-of-order and interleaved-event cases.

### QMP-5 — Asynchronous event stream
- **Project:** `Boxwright.Qmp`
- **Size:** S
- **Labels:** `qmp`
- **Depends on:** QMP-4
- **Description:** Expose unsolicited events (`SHUTDOWN`, `RESET`, `STOP`,
  `RESUME`, `POWERDOWN`, …) as a hot stream subscribers can consume without
  blocking the read loop.
- **Acceptance criteria:**
  - [ ] A subscriber receives events pushed by the `QMP-2` fixture, with name +
        data + timestamps.
  - [ ] Slow/absent subscribers never block reply correlation.
- **⚠ Risk / open question:** **Stream representation is unsettled by any ADR.**
  Architecture §4.2 sketches `IObservable<QmpEvent>`, but a usable observable
  (`Subject<T>`) means taking a **`System.Reactive`** dependency — at odds with
  the csproj's "no package references needed" intent for a clean NuGet surface.
  Decide between: (a) `System.Reactive`, (b) a minimal hand-rolled observable,
  or (c) `IAsyncEnumerable<QmpEvent>` / a plain `event`. Record the choice (worth
  a short ADR, since it shapes the public API).

### QMP-6 — Typed `execute` + status query wrappers
- **Project:** `Boxwright.Qmp`
- **Size:** S
- **Labels:** `qmp`
- **Depends on:** QMP-4
- **Description:** `ExecuteAsync<TResult>` convenience that deserializes the
  `return` payload, plus thin typed wrappers for `query-status` and `query-name`.
- **Acceptance criteria:**
  - [ ] `ExecuteAsync<TResult>` deserializes a `return` payload to a typed result
        against `QMP-2`.
  - [ ] `query-status` wrapper returns running/paused/etc. as a typed value.

### QMP-7 — `query-qmp-schema` capability probe
- **Project:** `Boxwright.Qmp`
- **Size:** S
- **Labels:** `qmp`
- **Depends on:** QMP-4
- **Description:** Fetch `query-qmp-schema` once after connect and expose it so
  `Core` can feature-detect the installed QEMU's capabilities instead of guessing
  (architecture §4.2).
- **Acceptance criteria:**
  - [ ] After connect, the schema is retrievable via a public API.
  - [ ] A helper answers "is command/feature X supported?" from the parsed
        schema, unit-tested against a canned schema in `QMP-2`.

### QMP-8 — Publish `Boxwright.Qmp` to NuGet
- **Project:** `Boxwright.Qmp` + build
- **Size:** M
- **Labels:** `qmp` `nuget` `build`
- **Depends on:** QMP-3, QMP-4, QMP-5, QMP-6, QMP-7, BLD-2
- **Description:** Ship the package as soon as it works (roadmap Stage 1 +
  ADR-0007) — an instant ecosystem contribution and discovery channel. Finalize
  package metadata, add a package README + symbols, and a CI release job.
- **Acceptance criteria:**
  - [ ] Placeholder metadata in `Directory.Build.props`
        (`RepositoryUrl`/`PackageProjectUrl` = `your-org`) is corrected to the
        real repo.
  - [ ] `dotnet pack` produces a package with a README, license (MIT), symbols,
        and no dependency on `Core`/`App`/Avalonia (cross-checked by `BLD-3`).
  - [ ] A tagged CI release publishes to NuGet via a stored API key.
- **⚠ Risk / open question:** Package id ownership/reservation on NuGet and the
  release-signing/key handling are operational unknowns; confirm the id is
  available before announcing.

---

## `Boxwright.Core` — orchestration / domain

> Knows what a VM is; knows nothing about pixels. May depend on `Qmp`; **must
> not** depend on Avalonia (Directive 4/8). All command-line generation lives in
> `CommandLineBuilder` (ADR-0001/0006). VM state lives **only** in the per-VM
> folder (ADR-0006).

### CORE-1 — `VmConfig` model + JSON load/save (schemaVersion 1)
- **Project:** `Boxwright.Core`
- **Size:** M
- **Labels:** `core`
- **Depends on:** BLD-1
- **Description:** Records mirroring architecture §9 (id, name, arch, machine,
  firmware bios/uefi, cpu, memoryMiB, disks, removableMedia, network +
  portForwards, display, `accelerator: "auto"`, boot). Load/save via
  `System.Text.Json` (source-gen preferred). `schemaVersion` stamped = 1.
- **Acceptance criteria:**
  - [ ] A config matching the architecture §9 example round-trips
        (deserialize → serialize) without loss.
  - [ ] `accelerator` persists as `"auto"`, **never** a concrete value like
        `"kvm"` (Directive 5 / ADR-0003).
  - [ ] An unknown/future `schemaVersion` is rejected with a clear error rather
        than silently mis-parsed.
  - [ ] Unit-tested.

### CORE-2 — QEMU invocation plumbing (binary locator + process-runner abstraction)
- **Project:** `Boxwright.Core`
- **Size:** S
- **Labels:** `core` `build`
- **Depends on:** BLD-1
- **Description:** Two small shared concerns every external-binary service needs:
  (1) locate `qemu-system-<arch>` and `qemu-img` — **bundled binaries in
  production, PATH fallback for dev** (ADR-0007); (2) an `IProcessRunner`
  abstraction over `System.Diagnostics.Process` so disk/accel/process services
  are unit-testable with a fake (no real QEMU). **No linking/P-Invoke — process
  boundary only** (Directive 2 / ADR-0005).
- **Acceptance criteria:**
  - [ ] Resolves bundled binaries when present; falls back to PATH for dev; gives
        a clear error when neither is found.
  - [ ] `IProcessRunner` exposes run-with-args, captured stdout/stderr, exit
        code, and cancellation; a fake implementation exists for tests.
  - [ ] Unit-tested with the fake (no real process launched).

### CORE-3 — VM folder repository (discover / load / save)
- **Project:** `Boxwright.Core`
- **Size:** M
- **Labels:** `core`
- **Depends on:** CORE-1
- **Description:** Per ADR-0006: one VM = one folder (config + disks + logs), no
  central registry/database/daemon. Discover VMs by scanning a VMs root,
  load/save their `VmConfig`, and create a new VM folder. Moving/copying a folder
  moves the VM.
- **Acceptance criteria:**
  - [ ] Scanning a directory of VM folders yields their configs.
  - [ ] Creating a VM writes a self-contained folder; no state is written to any
        app-global location (enforces ADR-0006 / CLAUDE.md §8).
  - [ ] Unit-tested against a temp directory.
- **⚠ Risk / open question:** The **default VMs root per OS** isn't specified by
  any ADR (e.g. `%LOCALAPPDATA%` vs `~/.local/share` vs `~/Library`). Pick
  sensible per-OS defaults, make it user-overridable, and note the choice.

### CORE-4 — `AcceleratorDetector` (kvm / hvf / whpx / tcg auto-select)
- **Project:** `Boxwright.Core`
- **Size:** M
- **Labels:** `core` `accel`
- **Depends on:** CORE-2
- **Description:** Detect the host accelerator at launch and **never hardcode it**
  (Directive 5 / ADR-0003): Linux → `kvm` if `/dev/kvm` is usable, macOS →
  `hvf`, Windows → `whpx` if the WHPX feature is present, with `tcg` as the
  universal fallback. Expose the resolved value for the UI to surface.
- **Acceptance criteria:**
  - [ ] Returns the correct platform accelerator on each OS, and `tcg` when no HW
        accel is available.
  - [ ] The resolved accelerator is exposed so the UI can show it (`APP-2`).
  - [ ] Detection logic is unit-tested via the `IProcessRunner`/filesystem fakes
        (`CORE-2`) — no dependence on the dev machine's real capabilities.
- **⚠ Risk / open question:** Reliable WHPX (Windows) and HVF (macOS) detection
  is non-trivial — WHPX may need `kernel-irqchip=off` and can conflict with
  VirtualBox/Hyper-V (architecture §5; feeds from `S0-2`). Define what "usable"
  means (probe vs. trial-launch) and fail safe to `tcg`.

### CORE-5 — `CommandLineBuilder` (VmConfig → QEMU args)
- **Project:** `Boxwright.Core`
- **Size:** L
- **Labels:** `core` `accel` `disk` `display`
- **Depends on:** CORE-1, CORE-4
- **Description:** The single place that translates a `VmConfig` into a
  `qemu-system-<arch>` argument list (ADR-0001 names this the correctness surface
  we own). Covers `-machine`, `-smp`, `-m`, `-accel <detected>`, drives
  (`-drive`/`-blockdev`, **virtio by default**), removable media (CD-ROM/ISO),
  NIC (`-netdev user` **default, no admin**, with `hostfwd` port-forwards),
  `-spice`, firmware (bios vs. **UEFI/OVMF**), boot order, and the per-launch QMP
  endpoint (TCP on Windows, Unix socket on Linux/macOS — see `CORE-8`).
- **Acceptance criteria:**
  - [ ] Golden-file unit tests assert exact arg lists for representative configs
        (BIOS+virtio disk, UEFI, ISO-boot, port-forward, TCG fallback).
  - [ ] Defaults to **user-mode (SLIRP) networking** — the default path needs no
        admin/root (Directive / architecture §7).
  - [ ] Uses the accelerator from `CORE-4`; never emits a hardcoded `-accel kvm`.
  - [ ] Builder is pure (config in → args out), no process launching, fully
        unit-tested without QEMU.
- **⚠ Risk / open question:** **UEFI firmware (OVMF/edk2) path** differs per
  platform and must point at the bundled firmware (`PKG-1`). Decide how the
  firmware file is located/passed (`-bios` vs. `pflash` `-drive if=pflash`) and
  whether MVP guests default to UEFI or BIOS. Not settled by an ADR.

### CORE-6 — `DiskService` (create / info via `qemu-img`)
- **Project:** `Boxwright.Core`
- **Size:** M
- **Labels:** `core` `disk`
- **Depends on:** CORE-1, CORE-2
- **Description:** Wrap `qemu-img` as a subprocess (architecture §3.3):
  `qemu-img create -f qcow2 …` (qcow2 default) and
  `qemu-img info --output=json …` (parse JSON). Snapshots/convert are **Stage 2**
  — not in MVP.
- **Acceptance criteria:**
  - [ ] Creates a qcow2 disk of a requested size via `qemu-img`.
  - [ ] Parses `qemu-img info --output=json` into a typed result.
  - [ ] Unit-tested against the `IProcessRunner` fake (canned `qemu-img` output);
        no real `qemu-img` required.

### CORE-7 — `QemuProcess` (spawn / supervise + per-VM log capture)
- **Project:** `Boxwright.Core`
- **Size:** M
- **Labels:** `core`
- **Depends on:** CORE-2, CORE-5
- **Description:** Spawn `qemu-system-<arch>` from the built args (`CORE-5`) via
  `System.Diagnostics.Process`, capture stdout/stderr to a **per-VM log file**
  in the VM folder, track running/exited state, and expose process exit. One VM =
  one process (ADR-0003); killing the process stops the VM.
- **Acceptance criteria:**
  - [ ] Launches a process, captures its output to the per-VM log, and reports
        exit (code + cause).
  - [ ] A failed launch (bad binary/args) surfaces a clear error including the
        captured stderr.
  - [ ] State transitions (starting/running/stopped) are observable for the UI.
  - [ ] Lifecycle logic unit-tested via the `IProcessRunner` fake.

### CORE-8 — QMP endpoint allocation + connect wiring
- **Project:** `Boxwright.Core`
- **Size:** M
- **Labels:** `core` `qmp`
- **Depends on:** CORE-7, QMP-4, QMP-5
- **Description:** The integration glue (architecture §3.2 steps 2–4): allocate a
  free per-launch QMP endpoint (free TCP port on Windows; Unix socket path on
  Linux/macOS), feed it into `CORE-5`'s args, and after spawn connect
  `IQmpClient` — handling the **startup race** (the socket isn't ready the
  instant the process starts) with bounded retry/backoff. Owns retry policy
  (which `Qmp` deliberately does not).
- **Acceptance criteria:**
  - [ ] Allocates a non-colliding endpoint per launch for the right platform.
  - [ ] Connect retry/backoff is unit-tested against the `QMP-2` fake (succeeds
        after N failed attempts; gives up cleanly after a timeout).
  - [ ] A documented **manual** integration smoke test connects to a real QEMU
        end-to-end (cannot be a pure unit test).
- **⚠ Risk / open question:** The socket-ready race and connect timing are
  fiddly and OS-dependent (TCP vs. AF_UNIX). Pick a bounded, logged retry rather
  than a fixed sleep.

### CORE-9 — VM lifecycle / power service (start / stop / pause / reset)
- **Project:** `Boxwright.Core`
- **Size:** M
- **Labels:** `core` `qmp`
- **Depends on:** CORE-8
- **Description:** Map user power actions to process + QMP operations: **start**
  (spawn + connect, `CORE-7`/`CORE-8`), **stop** (`system_powerdown` graceful,
  with force-`quit`/kill fallback), **pause** (`stop`) / **resume** (`cont`),
  **reset** (`system_reset`). Reflect QMP events (`SHUTDOWN`, etc.) back into VM
  state. This is the surface `App` binds to.
- **Acceptance criteria:**
  - [ ] Each action issues the correct QMP command (or process kill for force),
        verified against the `QMP-2` fake.
  - [ ] A `SHUTDOWN`/process-exit event returns the VM to `stopped` and tears
        down the socket.
  - [ ] No UI/Avalonia types appear here (checked by `BLD-3`).

### CORE-10 — `DisplayLauncher` (external `remote-viewer` / SPICE)
- **Project:** `Boxwright.Core`
- **Size:** S
- **Labels:** `core` `display`
- **Depends on:** CORE-5
- **Description:** Per ADR-0004 (MVP): QEMU runs a SPICE server (`-spice`, from
  `CORE-5`); locate and launch the external **`remote-viewer`** against the VM's
  SPICE endpoint. **Do not** embed a display or QEMU's SDL/GTK window (Directive
  7).
- **Acceptance criteria:**
  - [ ] Locates `remote-viewer` (bundled or on PATH) and launches it against the
        VM's SPICE host/port.
  - [ ] A missing `remote-viewer` produces a clear, actionable message (it does
        not crash or hang).
- **⚠ Risk / open question:** ADR-0004 leaves "bundled **or** documented per OS"
  open. For the `<10-minute on Windows` exit gate (`GATE-1`), requiring a
  separate virt-viewer install is real friction — decide whether MVP **bundles**
  virt-viewer on Windows (likely yes) and resolve with `PKG-1`/`PKG-2`.

---

## `Boxwright.App` — Avalonia GUI (MVVM)

> Views + viewmodels only. **No QEMU/process/QMP logic** — bind to `Core`
> (Directive / CLAUDE.md §8). MVVM via `CommunityToolkit.Mvvm`.

### APP-1 — Scaffold the Avalonia app shell
- **Project:** `Boxwright.App`
- **Size:** M
- **Labels:** `app` `build`
- **Depends on:** BLD-1
- **Description:** Add the Avalonia packages (`Avalonia`, `Avalonia.Desktop`,
  `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter`, `Avalonia.Diagnostics`
  [Debug]) + `CommunityToolkit.Mvvm`, create `App.axaml`/entry point, the main
  window, the **`app.manifest`** the csproj already references, theme, and a
  minimal DI/bootstrapping seam to `Core` services.
- **Acceptance criteria:**
  - [ ] `dotnet run --project src/Boxwright.App` opens an empty main window on
        Windows and Linux (and macOS where available).
  - [ ] `app.manifest` exists (csproj references it) and the app builds clean.
  - [ ] A DI/composition root resolves `Core` services into viewmodels.
- **⚠ Risk / open question:** Avalonia major version (11.x vs 12.x, per ADR-0002)
  should be pinned consistently with the `BLD-1` TFM choice.

### APP-2 — VM list view + viewmodel
- **Project:** `Boxwright.App`
- **Size:** M
- **Labels:** `app`
- **Depends on:** APP-1, CORE-3, CORE-9
- **Description:** List VMs from the repository (`CORE-3`), show per-VM state
  (stopped/running/paused from `CORE-9`), and **surface the resolved
  accelerator** (ADR-0003 requires showing it so users understand performance).
- **Acceptance criteria:**
  - [ ] VMs discovered by `CORE-3` appear with name + live state.
  - [ ] The resolved accelerator (kvm/hvf/whpx/**tcg**) is visible per VM/host.
  - [ ] Viewmodel logic unit-tested where practical (no live QEMU).

### APP-3 — VM power controls + honest status messaging
- **Project:** `Boxwright.App`
- **Size:** M
- **Labels:** `app`
- **Depends on:** APP-2, CORE-9
- **Description:** Start / stop / pause / reset / delete buttons wired to
  `CORE-9`. Surface launch/accel failures as **clear UI messages** (Directive 4:
  degrade gracefully) and be **honest about performance** — when the accelerator
  falls back to `tcg`, or on Windows/WHPX, say so plainly (Directive 9 /
  architecture §5). No marketing copy promising parity.
- **Acceptance criteria:**
  - [ ] Each control invokes the matching `CORE-9` operation and reflects the
        resulting state.
  - [ ] A failed start (e.g. WHPX unavailable) shows an actionable message rather
        than failing silently.
  - [ ] `tcg`-fallback / Windows performance reality is communicated honestly in
        the UI.

### APP-4 — New-VM flow with sensible defaults
- **Project:** `Boxwright.App`
- **Size:** L
- **Labels:** `app` `disk`
- **Depends on:** APP-2, CORE-1, CORE-3, CORE-6
- **Description:** Guided create flow (name, RAM, cores, disk size, firmware,
  with good defaults). On finish: create the VM folder + `VmConfig` (`CORE-3`)
  and the qcow2 disk (`CORE-6`). This is the largest single `App` piece.
- **Acceptance criteria:**
  - [ ] Completing the flow produces a bootable VM definition: a folder with a
        valid `VmConfig` and a created qcow2 disk.
  - [ ] Defaults are sane and overridable; basic validation (e.g. RAM/disk > 0,
        unique name) is enforced.
  - [ ] New VM immediately appears in the list (`APP-2`).

### APP-5 — ISO mount + boot
- **Project:** `Boxwright.App`
- **Size:** M
- **Labels:** `app`
- **Depends on:** APP-4, CORE-1, CORE-5
- **Description:** Let the user attach an installer ISO (CD-ROM removable media in
  `VmConfig`) and set boot order so the VM boots from it — the path to actually
  installing Ubuntu for the exit gate.
- **Acceptance criteria:**
  - [ ] Selecting an ISO records it in `removableMedia` and the built command line
        boots from CD when configured.
  - [ ] Booting a VM with an attached Ubuntu ISO reaches the installer (verified
        in the `GATE-1` end-to-end run).

### APP-6 — VM settings panel (existing VM)
- **Project:** `Boxwright.App`
- **Size:** M
- **Labels:** `app`
- **Depends on:** APP-2, CORE-1
- **Description:** Edit an existing VM's config (RAM, cores, disks, removable
  media, networking incl. optional port-forwards, display, firmware). Make clear
  that boot-time settings require a **stop + relaunch** to take effect
  (architecture §3.1 — there is no QMP call to change boot CPU/RAM).
- **Acceptance criteria:**
  - [ ] Edits persist to the VM's `VmConfig` via `CORE-3`.
  - [ ] The UI indicates which changes require a VM restart.
  - [ ] Editing while running does not corrupt the config or the live process.

### APP-7 — "Open display" button → SPICE viewer
- **Project:** `Boxwright.App`
- **Size:** S
- **Labels:** `app` `display`
- **Depends on:** APP-3, CORE-10
- **Description:** A button on a running VM that calls `CORE-10` to launch
  `remote-viewer` against the VM's SPICE endpoint.
- **Acceptance criteria:**
  - [ ] Clicking it opens the SPICE viewer showing the running guest.
  - [ ] If `remote-viewer` is unavailable, the user sees `CORE-10`'s actionable
        message.

---

## Packaging & distribution

> Bundle QEMU per platform, ship it **unmodified** as a subprocess, satisfy GPL
> obligations (ADR-0005/0007). **Windows is the gate-critical target**
> (`GATE-1`); macOS signing is the long pole.

### PKG-1 — Bundle QEMU on Windows (+ locator resolves it)
- **Project:** cross-cutting/build
- **Size:** M
- **Labels:** `build` `packaging`
- **Depends on:** CORE-2, APP-1
- **Description:** Bundle `qemu-system-*` + `qemu-img` (and the **UEFI/OVMF
  firmware**, and — pending `CORE-10`'s decision — `remote-viewer`) for Windows,
  and make `CORE-2`'s locator resolve the bundled copies. On Linux, dev/runtime
  may use system or Flatpak QEMU (`PKG-3`).
- **Acceptance criteria:**
  - [ ] A packaged Windows build runs VMs using **bundled** QEMU with no
        user-installed QEMU on PATH.
  - [ ] Bundled QEMU is **unmodified** (feeds `PKG-5`).
  - [ ] Firmware (and viewer, if bundled) are found by the locator at runtime.
- **⚠ Risk / open question:** Which Windows QEMU build (e.g. weilnetz), the
  +80–200 MB size hit (architecture §10), and exact firmware/viewer layout are
  unresolved. The virt-viewer bundling decision (`CORE-10`) lands here.

### PKG-2 — Windows installer artifact (MSI / zip)
- **Project:** cross-cutting/build
- **Size:** M
- **Labels:** `build` `packaging`
- **Depends on:** PKG-1, APP-4, APP-5, APP-7
- **Description:** Produce an installable Windows artifact (MSI or zip) that a
  stranger can download and run — the thing the exit gate measures. README/UI
  copy must stay honest about Windows performance (Directive 9).
- **Acceptance criteria:**
  - [ ] Double-click/extract install puts a runnable Boxwright (with bundled
        QEMU) on a clean Windows machine.
  - [ ] No admin/root required for the default (user-mode networking) path.
  - [ ] Built in CI and downloadable as a release asset.

### PKG-3 — Linux artifact (AppImage or Flatpak)
- **Project:** cross-cutting/build
- **Size:** M
- **Labels:** `build` `packaging`
- **Depends on:** APP-4, APP-5, APP-7
- **Description:** Produce a Linux artifact; bundle via Flatpak or rely on system
  QEMU per architecture §10.
- **Acceptance criteria:**
  - [ ] A Linux user can install/run the artifact and boot a VM.
  - [x] QEMU resolution (system vs bundled) is documented and works.
- **✅ Resolved (ADR-0011):** **AppImage** (Flatpak's sandbox fights spawning QEMU + `/dev/kvm` +
  remote-viewer) with **system QEMU** (not bundled — no GPL source-offer on Linux). Built by
  `tools/package-linux.sh` + `.github/workflows/package-linux.yml`. Remaining: the clean-Linux boot
  test (first criterion above).

### PKG-4 — macOS signed `.app` (hypervisor entitlement + notarization)
- **Project:** cross-cutting/build
- **Size:** L
- **Labels:** `build` `packaging`
- **Depends on:** APP-4, APP-5, APP-7
- **Description:** A `.app` bundling a **signed** QEMU with the
  `com.apple.security.hypervisor` entitlement (required for HVF), code-signed and
  notarized.
- **Acceptance criteria:**
  - [ ] The signed/notarized `.app` launches on a clean macOS without Gatekeeper
        blocking it, and boots a VM using HVF.
- **⚠ Risk / open question:** **Open per architecture §12** — the macOS
  code-signing/notarization pipeline for a hypervisor-entitled app is unsolved
  and the riskiest packaging item (needs an Apple Developer account, entitlement
  + signing of the bundled QEMU). **Not required for `GATE-1`** (Windows-only);
  can trail the Windows MVP. Flag early so it doesn't block the public Windows
  ship.

### PKG-5 — GPL hygiene release checklist
- **Project:** cross-cutting/build
- **Size:** S
- **Labels:** `build` `license` `packaging`
- **Depends on:** PKG-1 (and PKG-3/PKG-4 as those land)
- **Description:** Per ADR-0005: every release that bundles QEMU ships it
  **unmodified**, accompanied by the corresponding source (or a written offer +
  upstream link) and the QEMU/virt-viewer license texts; Boxwright's own code
  stays MIT. Encode this as a repeatable release checklist.
- **Acceptance criteria:**
  - [ ] Each platform release bundle includes QEMU's license + a source/offer
        link, kept with the binaries.
  - [ ] A checklist in the repo gates releases on these obligations.
  - [ ] No linking/P-Invoke/vendoring of QEMU source exists (cross-checks
        Directive 2).

---

# 🏁 GATE-1 — MVP exit gate (Go / No-Go)

- **Project:** milestone
- **Size:** —
- **Labels:** `gate`
- **Depends on:** PKG-2 (and all `APP-*`, `CORE-*`, `QMP-*` it transitively
  requires)
- **Gate question (from the roadmap):** *Can a stranger install it on Windows and
  boot Ubuntu in under 10 minutes?*
- **Acceptance criteria:**
  - [ ] Someone who did **not** build Boxwright, on a clean Windows machine,
        installs the `PKG-2` artifact, creates a VM, attaches an Ubuntu ISO, and
        reaches the Ubuntu installer — **start to boot in under 10 minutes**.
  - [ ] No manual QEMU install and no command line are required of them.
  - [ ] The display opens via the SPICE viewer (`APP-7`); the resolved
        accelerator is shown honestly.
- **Verdict:**
  - **Pass →** ship publicly and proceed to Stage 2.
  - **Fail →** fix the friction *before* adding any new feature (roadmap rule).

---

## Out of scope (Stage 2+)

Explicitly **not** planned here — revisited after the gates so planning effort
isn't wasted (roadmap Stages 2–3 / v1.0):

- Built-in OS catalog & one-click ISO download; checksum/signature verification
  & provenance.
- **virtio-win** auto-attach for Windows guests.
- qcow2 snapshots (create/list/revert/delete) and live/external snapshots.
- Embedded **VNC** (v0.3) and embedded **SPICE** (v1.0) display — MVP stays on
  external `remote-viewer` (ADR-0004).
- USB passthrough (UsbDk), bridged/TAP networking, QMP `query-stats`
  performance graphs.
- VM templates / linked clones, headless CLI parity, plugin/recipe API.

> Per the roadmap's #1 survival rule: **resist scope creep.** No clustering, no
> fleet management, ever.

---

## Open questions & risks (roll-up)

| Item | Open question / risk | Settled by ADR? |
|------|----------------------|-----------------|
| `S0-2`, `CORE-4` | WHPX usable on target Windows? Conflicts (VirtualBox/Hyper-V), `kernel-irqchip=off`; reliable detection. | No — architecture §5 only describes it |
| `BLD-1` | Target framework: `net8.0` floor vs. `net10.0` LTS. | No |
| `QMP-5` | Event-stream API: `System.Reactive` vs. hand-rolled observable vs. `IAsyncEnumerable`/`event` (clean NuGet surface tension). | No — worth a short ADR |
| `QMP-4` | Concurrency: id-correlation + event demux + teardown in the read loop. | N/A (impl risk) |
| `CORE-3` | Default VMs root directory per OS. | No |
| `CORE-5` | UEFI/OVMF firmware location & `-bios` vs `pflash`; UEFI-vs-BIOS default. | No |
| `CORE-8` | QMP socket-ready startup race; TCP vs AF_UNIX timing. | N/A (impl risk) |
| `CORE-10`, `PKG-1` | Bundle `remote-viewer`/virt-viewer on Windows vs. require install (affects 10-min gate). | No — ADR-0004 leaves it open |
| `PKG-3` | Linux: AppImage vs Flatpak. | No |
| `PKG-4` | macOS code-signing/notarization for a hypervisor-entitled app. | No — open per architecture §12 |
| `QMP-8` | NuGet package-id ownership; release key handling. | N/A (ops) |

---

## Suggested critical path

The shortest dependency chain to the Windows exit gate (parallelizable work
omitted for clarity):

```
GATE-0
  └─ BLD-1 ─ BLD-2/BLD-3
       ├─ QMP-1 ─ QMP-2 ─ QMP-3 ─ QMP-4 ─ QMP-5 (─ QMP-8 publish)
       └─ CORE-1 ─ CORE-2
                    ├─ CORE-4 ─┐
                    │          └─ CORE-5 ─ CORE-7 ─ CORE-8(+QMP-4/5) ─ CORE-9
                    ├─ CORE-3                                          │
                    ├─ CORE-6 ───────────────────────────────┐        │
                    └─ CORE-10 (needs CORE-5) ────────────┐   │        │
                                                          ▼   ▼        ▼
   APP-1 ─ APP-2 ─ APP-3 ─ APP-4 ─ APP-5 ─ APP-6 ─ APP-7
                                                  │
                            PKG-1 ─ PKG-2 ────────┴──────────────► 🏁 GATE-1
                            (PKG-3 Linux, PKG-4 macOS, PKG-5 GPL: parallel;
                             only PKG-2/Windows is required for GATE-1)
```

Parallelism a solo dev can exploit: the **`Qmp` track** (`QMP-1…QMP-8`) and the
non-QMP **`Core` track** (`CORE-1`, `CORE-3`, `CORE-6`, and `CORE-4`/`CORE-5`)
are largely independent until `CORE-8` joins them. `App` work (`APP-1`) can begin
on stub data right after `BLD-1`.

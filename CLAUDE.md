# CLAUDE.md

This file is the operating manual for AI coding agents (Claude Code and similar)
working in this repository. **Read it fully before writing code.** It is
intentionally concise and high-signal. Depth lives in `docs/`.

---

## 1. What this project is

**Boxwright** is a cross-platform desktop GUI for **QEMU** — a "VirtualBox-like"
application where QEMU is the virtualization backend. It targets **Windows,
macOS, and Linux** with equal weight. The goal is the ease of Quickemu (pick an
OS, click, boot) with the polish of a VirtualBox-style UI, and **no daemon, no
Oracle account, no Broadcom portal**.

> `Boxwright` is a working codename. If it changes, it is a single global
> find-replace across namespaces, project names, and docs.

**Status:** greenfield / pre-MVP. There is no legacy to preserve. Decisions are
captured as ADRs in `docs/adr/`.

**Audience for the product:** developers, students, homelab/distro-hoppers who
want one tool that behaves the same on every desktop OS.

---

## 2. Prime Directives (non-negotiable)

These encode hard-won conclusions from the design research. **Do not violate
them without an ADR that supersedes the relevant one.** If a request conflicts
with a directive, stop and flag it rather than quietly working around it.

1. **Control QEMU as a child process via QMP.** Each VM is a
   `qemu-system-<arch>` process. We talk to it with **QMP** (JSON-RPC over a
   TCP or Unix socket) and manage disks via the **`qemu-img`** subprocess. See
   ADR-0001 and ADR-0003.

2. **Never link or embed QEMU source. Always invoke it as a separate process.**
   QEMU is GPLv2; our code is MIT. Process isolation keeps the licenses clean.
   No P/Invoke into QEMU, no static linking, no vendored QEMU source. See
   ADR-0005.

3. **`libvirt` is NOT the backend.** libvirt is effectively Linux-only and would
   destroy cross-platform parity. Do not introduce a libvirt dependency. See
   ADR-0001.

4. **Cross-platform parity is sacred.** Any feature must work on Windows, macOS,
   and Linux, or be explicitly capability-gated and degrade gracefully with a
   clear UI message. Reject any design that is silently Linux-only.

5. **Acceleration is detected per-OS, never hardcoded.** Linux → `kvm`,
   macOS → `hvf`, Windows → `whpx`, with `tcg` as the universal software
   fallback. Detection and selection are automatic. Never assume `kvm`. See
   ADR-0003 and `docs/architecture.md`.

6. **One VM = one JSON config file = one QEMU process. No global daemon, no
   background service.** Config files are human-readable and portable (copy the
   folder = move the VM). See ADR-0006.

7. **Display: MVP shells out to `remote-viewer` (SPICE).** Do NOT try to embed
   QEMU's built-in SDL/GTK window. An embedded VNC/SPICE client is a later
   milestone, not MVP. See ADR-0004.

8. **`Boxwright.Qmp` stays GUI-agnostic and independently shippable to NuGet.**
   No Avalonia, no `Boxwright.Core`, no UI concepts may leak into the QMP
   library. It is a standalone QEMU/QMP client that happens to live in this
   monorepo. See ADR-0002 and ADR-0007.

9. **Be honest about performance.** On Windows, QEMU is slower than VMware. The
   GUI cannot fix that. Do not add copy that promises otherwise.

---

## 3. Tech stack

- **Language:** C# (latest stable `LangVersion`), nullable reference types **on**.
- **Runtime:** target the current .NET LTS. .NET 8 is the floor; .NET 10 is the
  latest LTS as of 2026. Pin the exact `TargetFramework` in
  `Directory.Build.props`, not in individual files.
- **UI:** **Avalonia UI** (11.x/12.x), MVVM. See ADR-0002.
- **MVVM toolkit:** `CommunityToolkit.Mvvm` (source-generated observables /
  commands). ReactiveUI is acceptable in the App layer only if a maintainer
  decides so — keep it out of `Core` and `Qmp`.
- **QMP / JSON:** `System.Text.Json` only. No Newtonsoft.
- **Process / disks:** `System.Diagnostics.Process` + `qemu-img`.
- **Tests:** xUnit. The QMP client is tested against an in-memory/loopback fake
  socket, not a live QEMU.
- **Lint/format:** `.editorconfig` is the single source of truth. Run
  `dotnet format` before committing.

---

## 4. Repository layout

```
boxwright/
├── CLAUDE.md                 ← you are here
├── README.md                 ← public, human-facing
├── CONTRIBUTING.md
├── LICENSE                   ← MIT
├── Directory.Build.props     ← shared MSBuild settings (TFM, nullable, etc.)
├── .editorconfig             ← canonical code style
├── docs/
│   ├── architecture.md       ← the deep design (read before backend work)
│   ├── roadmap.md            ← phased plan + go/no-go gates
│   ├── conventions.md        ← coding + commit + PR conventions
│   ├── glossary.md           ← QMP, QGA, SPICE, HVF, WHPX, qcow2, …
│   └── adr/                  ← Architecture Decision Records (the "why")
├── src/
│   ├── Boxwright.Qmp/        ← QEMU Machine Protocol client (NuGet-publishable)
│   ├── Boxwright.Core/       ← domain: VM model, config, process mgmt, accel detect
│   └── Boxwright.App/        ← Avalonia GUI (views + viewmodels only)
└── tests/
    ├── Boxwright.Qmp.Tests/
    └── Boxwright.Core.Tests/
```

**Dependency direction (must not be violated):**

```
Boxwright.App   ──►  Boxwright.Core  ──►  Boxwright.Qmp
   (UI/MVVM)          (orchestration)        (protocol)
```

- `Qmp` depends on nothing of ours.
- `Core` may depend on `Qmp`. It must NOT depend on Avalonia.
- `App` may depend on `Core` (and `Qmp`). No QEMU/process/business logic lives
  in `App` — views and viewmodels only.

---

## 5. Build, run, test

First-time scaffolding (when the projects are still stubs):

```bash
dotnet new sln -n Boxwright
dotnet sln add src/Boxwright.Qmp src/Boxwright.Core src/Boxwright.App \
               tests/Boxwright.Qmp.Tests tests/Boxwright.Core.Tests
```

Everyday commands:

```bash
dotnet restore
dotnet build
dotnet test
dotnet format                       # apply .editorconfig style
dotnet run --project src/Boxwright.App
```

Running a VM requires a QEMU install on PATH during development (Linux:
distro package; macOS: Homebrew; Windows: qemu.weilnetz.de build). Production
builds bundle QEMU per platform — see ADR-0007.

---

## 6. How to work in this repo

- **Before backend changes**, read `docs/architecture.md`. Before touching the
  protocol layer, also skim the QMP spec linked in `docs/glossary.md`.
- **Before a significant design decision**, check `docs/adr/`. If your change
  contradicts an ADR, propose a new ADR that supersedes it rather than diverging
  silently.
- **New architectural decision?** Add `docs/adr/NNNN-title.md` using the format
  in `docs/adr/README.md`. Keep it short.
- **Keep `Boxwright.Qmp` clean.** If you find yourself importing anything
  GUI- or app-specific into it, stop — that belongs in `Core` or `App`.
- **Match existing style.** `.editorconfig` + `docs/conventions.md` define it.
  Do not reformat unrelated code in a feature PR.
- **Tests with behavior.** New QMP commands or Core logic ship with xUnit tests.
  The QMP client must be testable without a running QEMU.

---

## 7. Definition of done (per change)

- Builds clean on the target TFM with no new warnings.
- `dotnet format` produces no diff.
- New/changed behavior covered by tests where practical.
- Cross-platform implications considered (see Directive 4). If a feature is
  OS-specific, it is gated and the UI explains the limitation.
- If a Prime Directive or ADR is affected, an ADR is added or updated.
- Public, user-facing strings are honest about capability and performance.

---

## 8. Anti-patterns — do NOT do these

- ❌ Adding a `libvirt` dependency or a "libvirt mode."
- ❌ Hardcoding `-accel kvm` (breaks macOS/Windows).
- ❌ Linking, P/Invoking, or vendoring QEMU source (GPL contamination).
- ❌ Embedding QEMU's SDL/GTK window inside Avalonia.
- ❌ Putting QEMU/process/QMP logic inside `Boxwright.App`.
- ❌ Letting Avalonia or app types leak into `Boxwright.Qmp` or `Boxwright.Core`.
- ❌ A background daemon or system service to "manage" VMs.
- ❌ Newtonsoft.Json (use `System.Text.Json`).
- ❌ Requiring admin/root for the default (user-mode networking) path.
- ❌ Storing VM state anywhere other than the per-VM folder + its JSON config.
- ❌ Marketing copy claiming Windows performance parity with VMware/VirtualBox.

---

## 9. Pointers

- Why direct-QMP instead of libvirt → `docs/adr/0001-direct-qmp-not-libvirt.md`
- Why Avalonia → `docs/adr/0002-avalonia-ui.md`
- Process & acceleration model → `docs/adr/0003-process-per-vm.md`
- Display strategy → `docs/adr/0004-display-via-remote-viewer.md`
- License hygiene → `docs/adr/0005-gpl-hygiene-subprocess.md`
- VM config format → `docs/adr/0006-json-vm-config.md`
- Bundling QEMU & the QMP library → `docs/adr/0007-bundled-qemu-and-qmp-library.md`
- The full design → `docs/architecture.md`
- The plan & gates → `docs/roadmap.md`

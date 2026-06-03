# ADR-0014: Persist VM runtime state; re-adopt running QEMU on restart

- **Status:** Accepted
- **Date:** 2026-06-03

## Context
Boxwright shuts its VMs down gracefully when the window is closed (`MainWindow.OnClosing`). But when the
app is terminated **abnormally** — Ctrl+C in the `dotnet run` terminal, a crash, a Task-Manager kill —
that handler never runs, and because the OS does **not** kill child processes with their parent (true on
Windows especially), the `qemu-system-*` children keep running. A fresh Boxwright had no memory of them,
so it showed those VMs as "Stopped" and the user had to kill them in Task Manager — orphaned, unmanageable
processes. We want VMs to survive the app and be **re-managed** on restart (the VirtualBox behaviour), but
**without a daemon** (Directive 6).

Exploration confirmed the pieces were already reconnect-friendly: `IRunningProcess.Id` exposes the PID;
`IQmpConnector.ConnectAsync(endpoint, isAlive, …)` connects to any already-listening QMP socket; QEMU's
`-qmp …,server,nowait` accepts a fresh connection after the previous client died; and the UI shows a VM as
Running purely from a non-null session.

## Decision
- **Persist a per-VM `runtime.json`** (in the VM folder beside `vm.json` — ADR-0006-compliant; ephemeral
  session state, kept separate from the persistent `vm.json`). It records the **PID**, **QMP endpoint**,
  **display port + protocol**, **guest-agent port**, and **accelerator** — everything needed to rebuild the
  session without re-allocating or re-detecting. `VmLauncher` writes it on a successful launch.
- **Re-adopt on startup** (and on every list refresh, idempotently): for each VM with a `runtime.json`
  whose recorded PID is still a live `qemu-system-*` process, reconnect the QMP client and present the VM as
  Running. This reuses the **same `RunningVm`** control surface, so power actions / display / guest agent
  all work on the re-adopted VM.
- **Attach by PID** via `IProcessLauncher.Attach` + an `AttachedProcess` that wraps the process and detects
  exit by **polling** (~2 s) — a non-child process can't be exit-notified by event cross-platform.
  `QemuProcess.Attach` adopts it with **no stdout capture** (the original pipe died with the launching
  process). A `qemu-system-*` name check guards against PID reuse.
- **Clear `runtime.json` when the VM stops** — `RunningVm` invokes an `onStopped` callback on dispose; the
  reconnector also clears a stale record when the PID is gone. An app-kill leaves a stale file that the next
  startup resolves.
- **No daemon, no service** — the design is "state on disk + reconnect on launch", consistent with Directive
  6. `runtime.json` is the only new artifact and lives in the per-VM folder.
- **Leave, don't kill, on a half-adopt** — if the process is alive but its QMP socket won't reconnect, the
  process is left running (we never hard-kill the user's VM); it just isn't adopted this run.

## Consequences
- **Easier:** closing or crashing Boxwright no longer orphans VMs — they keep running and are re-adopted on
  restart, fully controllable again. VirtualBox-like, without a background service.
- **Harder / accepted:** a re-adopted VM has **no live log** (its `qemu.log` is frozen at the previous
  process's death; QMP control is unaffected); PID reuse is guarded only by the process name; exit detection
  for adopted VMs has ~2 s latency (polling); a single running instance is assumed (a second instance's
  adopt fails cleanly because QMP allows one connection).
- **Honest (Directive 9):** the orphan problem is **mitigated, not 100 % eliminated** — a crash in the tiny
  window before `runtime.json` is written, or a process whose QMP socket is unreachable, still can't be
  re-adopted. The Windows **Job Object** backstop (below) would close that residual gap.

## Alternatives considered
- **Windows Job Object (`KILL_ON_JOB_CLOSE`)** — ties QEMU's lifetime to Boxwright's, so an abnormal exit
  kills the children (no orphans, clean tree). Rejected as the *primary* fix because it **loses** the VM on
  any app exit and risks guest-filesystem corruption (a hard kill); reconnect keeps VMs running and
  re-adopts them. It remains a sensible **complementary backstop** for the QMP-unreachable residual case.
- **A background daemon/service** to track and supervise VMs (VirtualBox's `VBoxSVC` model): rejected —
  Directive 6 forbids a daemon. On-disk state + reconnect achieves the same without one.
- **`Process.Exited` for adopted processes:** unreliable for non-child processes on Linux/macOS (you can't
  `waitpid` a non-child), so exit is detected by polling instead.

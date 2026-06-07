# ADR-0019: Live VM performance metrics (CPU / RAM / disk sparklines)

- **Status:** Accepted
- **Date:** 2026-06-06

## Context
A VirtualBox-style desktop VM tool is expected to answer "is my VM busy?" at a glance. Boxwright had no
live resource view. This adds **live CPU / RAM / disk-I/O sparklines** to the VM detail view while a VM
runs — visible polish that also exercises the `Boxwright.Qmp` client. The constraint: stay lean
(Directive — minimal dependencies) and cross-platform (Directive 4).

## Decision
- **Metrics sourced from the cheapest reliable place:**
  - **CPU %** and **RAM (RSS)** from the QEMU host process via `System.Diagnostics.Process`
    (`TotalProcessorTime`, `WorkingSet64`) — cross-platform, no QMP needed. CPU % is the process
    CPU-time delta over wall time across the VM's **allocated vCPUs** (so 100 % = the VM saturating its
    vCPUs), clamped to 0–100.
  - **Disk read/write MB/s** from QMP **`query-blockstats`** (cumulative `rd_bytes`/`wr_bytes` summed
    across block devices, differenced between samples). Network is deferred — QEMU exposes no simple
    cumulative net-byte counter over QMP.
- **A testable seam.** `IRunningVm.GetMetricsSampleAsync` returns a raw cumulative `VmMetricsSample`
  (`RunningVm` reads the process + `IQmpClient.QueryBlockStatsAsync`); the pure `VmMetrics.Derive` turns
  two samples + the wall interval into a `VmMetricsRate` (CPU %, RAM MB, disk MB/s) — unit-tested in
  isolation, so the view-model only orchestrates.
- **Polling.** `VmListItemViewModel` runs a fire-and-forget loop (mirroring the existing log-refresh
  pattern) ~once a second while the VM is Running/Paused, differencing samples and publishing fixed-length
  ring-buffer histories (reassigned `double[]` so the bound graph redraws) plus current-value labels.
  Updates are marshalled onto the UI thread via `IUiDispatcher`; the loop stops and the metrics reset when
  the VM leaves Running. Best-effort — a transient QMP/process error drops a sample, not the loop.
- **Hand-drawn sparkline, no charting dependency.** `Sparkline` is a ~60-line Avalonia `Control` that
  draws the series as a normalized polyline (optional fixed `Maximum`, e.g. 100 for CPU %). No new NuGet
  package — consistent with the lean ethos.

## Consequences
- **Easier:** an at-a-glance live resource view; reusable `QueryBlockStatsAsync` + `Sparkline`; the
  metrics math is pure + tested.
- **Harder / accepted:** CPU % is the *host process* view of the VM (not in-guest per-core), labelled as
  "% of allocated vCPUs" to set expectations; network throughput is deferred; the graph is a lightweight
  sparkline, not a full charting surface.

## Verification
- **Unit:** `VmMetricsTests` (CPU %, RAM, disk-rate math; clamping; zero-wall and zero-vCPU guards);
  `QmpClientQueryTests` (`QueryBlockStatsAsync` sums rd/wr across devices, tolerates a missing `stats`
  object); `VmListItemViewModelTests` (a Running VM with scripted samples populates the histories).
- **Live (real QEMU/WHPX):** a harness booted a real VM headless and sampled the exact paths
  (`QueryBlockStatsAsync` + process CPU/RSS + `VmMetrics.Derive`): `query-blockstats` returned real
  cumulative counters that advanced as the guest did I/O (registering a disk rate), CPU % tracked the
  process, and RAM read the working set (~540 MB) — confirming the data pipeline end to end. The sparkline
  rendering is plain Avalonia drawing.

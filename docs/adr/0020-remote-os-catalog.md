# ADR-0020: Remote / community OS catalog (remote → cache → bundled)

- **Status:** Accepted
- **Date:** 2026-06-06

## Context
ADR-0010 shipped the OS catalog ("pick an OS → click → boot") as a curated JSON **bundled** in
`Boxwright.Core`, explicitly behind `IOsCatalogSource` so "a remote manifest can implement the same
interface later with no caller changes." That day has come. A bundled-only catalog goes stale the moment a
distro publishes a point release (a new checksum), and the only fix is a full app rebuild + reinstall.
Stage 2's whole value is freshness and breadth of one-click OSes; v1.0 anticipates community-contributed OS
definitions. We want the catalog to **grow and stay fresh without shipping a new build**, while staying
honest (Directive 9), cross-platform (Directive 4), and never blocking the user when offline.

## Decision
- **A `RemoteOsCatalogSource` decorator behind the existing `IOsCatalogSource`.** It fetches the same
  `OsCatalog.json` that lives in the project repo, served raw over HTTPS
  (`raw.githubusercontent.com/<repo>/main/src/Boxwright.Core/OsCatalog.json`), via the existing
  `IHttpStreamSource`, and parses it with the existing `OsCatalogJson.Deserialize` (lenient JSONC +
  `schemaVersion` validation). No new dependency; callers (`CatalogViewModel`, the new-VM flow) are
  unchanged.
- **Degrade gracefully: remote → last-good cache → bundled.** A successful remote fetch is written
  atomically (temp file + move) to a last-good cache
  (`%LOCALAPPDATA%/Boxwright/os-catalog-cache.json` and OS equivalents). Any remote failure — offline,
  timeout, non-success status, malformed JSON — falls back **silently** to the cache, then to the bundled
  list, so the catalog UI always gets a usable list.
- **Default-on, best-effort, non-blocking.** The fetch runs automatically under a short timeout (~5 s); a
  failure never surfaces as an error. The first successful result is **memoized for the process lifetime**,
  so repeated catalog opens in one session don't re-hit the network. Only genuine cancellation by the
  caller propagates (it is not treated as a fallback). No settings toggle in this cut — the silent fallback
  makes one unnecessary.
- **Remote is authoritative when reachable.** The hosted file is the same source-of-truth file, just
  fresher, so a successful fetch **replaces** the bundled list rather than merging. Bundled is purely the
  offline fallback.

## Freshness check (refinement)
The original "fall back **silently**" rule was too blunt for one case: a cache served **only because the
remote was unreachable** can drift out of date (a distro point release changes the ISO URL/SHA-256), and a
silent stale cache is dishonest (Directive 9). So the silent rule is refined — **the cache fallback is no
longer silent once the cache is older than a configurable freshness window (default ~21 days).** In that
case the cache is *still served* (offline users keep a usable list — behaviour unchanged) but it is logged
as a warning and reported via a new `OsCatalogFreshness { State, CachedAtUtc, Age, IsStale }` snapshot
(`IOsCatalogFreshnessProvider`, forwarded by `CompositeOsCatalogSource`) so the CLI/GUI can tell the user.
Three states are distinguished: **Remote** (live this session), **FreshCache** (silent), **StaleCache**
(warned + flagged), and **Bundled** — the shipped baseline, which is **explicitly not "stale"** because it
is the floor, not an aged copy. The cache's refresh time is the cache file's `LastWriteTimeUtc`, stamped
from an injectable `TimeProvider` (default `TimeProvider.System`) so the check is deterministic and unit-
testable without a clock or network. The CLI `os list` prints a one-line note (e.g. `Catalog: cached 30
day(s) ago (stale; remote unreachable, ISO URLs/SHA-256 may be outdated).`); `--json` output is unchanged.

## Trust model
The catalog is **project-hosted over HTTPS** and grows by **pull requests to the repo** — the GitHub repo
+ TLS is the trust boundary, the same one a user already extends to the app binary. Crucially, this does
**not** weaken download integrity: every entry still pins a **SHA-256** that `IsoDownloader` verifies while
streaming (ADR-0010). So a tampered or hostile catalog can at worst change *which already-verified file* is
offered (and a wrong hash fails the download cleanly) — it cannot deliver an unverified ISO. GPG/PGP
*signing of the catalog itself* remains a possible fast-follow but is not required for this boundary.

## Consequences
- **Easier:** the catalog ships new OSes and corrected checksums without an app update; the community can
  contribute entries via PR; offline users still get a working (cached or bundled) list; the whole thing
  reuses `IHttpStreamSource` + `OsCatalogJson` and is fully unit-tested without network.
- **Harder / accepted:** a first-run, online user pays a small (timeout-bounded) fetch before the catalog
  appears; the bundled list can lag the remote (it's the floor, not the ceiling); trust rests on HTTPS +
  repo review (mitigated by the downstream SHA-256 gate). Network throughput/ETag caching and catalog
  signing are deferred.

## Alternatives considered
- **Remote-only (no bundle):** rejected — breaks offline use and first-run when the host is down; the
  bundle is a cheap, reliable floor.
- **Merge remote + bundled by id:** rejected for now — adds a two-source mental model and dedup logic for
  little gain, since the remote *is* the superset source-of-truth file. Replace is simpler and honest.
- **Opt-in toggle (off by default):** rejected — the silent remote→cache→bundled fallback already makes the
  feature safe and invisible on failure, so a setting would add surface without protecting anything.
- **Sign the catalog (GPG/PGP) now:** deferred — HTTPS + repo PRs + the downstream per-ISO SHA-256 already
  satisfy the integrity directive; catalog signing is a later hardening step.

Supersedes the "a remote manifest can implement the same interface later" note in
[ADR-0010](0010-os-catalog.md).

## Verification
- **Unit (`RemoteOsCatalogSourceTests`):** remote success returns remote entries **and** writes the cache;
  remote failure with a cache returns the cache (bundled untouched); remote failure with no cache returns
  bundled; malformed remote JSON falls back without throwing and does **not** overwrite the cache; an
  internal timeout falls back; caller cancellation propagates as `OperationCanceledException`; a second
  call is memoized (network touched exactly once). Freshness: a within-window cache is served silently
  (no warning); a remote-unreachable cache past the window is still served but flagged `StaleCache` and
  warned exactly once; a no-cache bundled fallback is `Bundled` and never stale; freshness is `Unknown`
  before the first load; the window boundary (age == window) is treated as fresh; the configurable window
  flips the same cache age between fresh/stale; and `CompositeOsCatalogSource` forwards the wrapped remote
  source's freshness. All against a fake `IHttpStreamSource` + fake bundled source + fake `TimeProvider`,
  no network.
- **Live:** pointed at the real hosted URL, the app fetches + parses the catalog and writes
  `os-catalog-cache.json`; with a bad URL it serves the cache; with the cache removed it serves the bundled
  list — none of which surfaces an error in the catalog UI.

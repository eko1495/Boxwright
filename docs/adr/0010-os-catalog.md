# ADR-0010: One-click OS catalog — bundled JSON + verified ISO download to a shared cache

- **Status:** Accepted
- **Date:** 2026-06-02

## Context
Stage 2's headline feature is "pick an OS → click → boot": a catalog of installable OSes
whose ISOs Boxwright downloads and turns into a ready-to-install VM. The roadmap left three
forces open: where the catalog comes from, where downloaded ISOs live, and how downloads are
trusted. Architecture §11 requires **checksum/signature verification + provenance**; Directive 4
requires cross-platform parity; ADR-0006 says VM state lives in the per-VM folder.

## Decision
- **Catalog = a curated JSON bundled in `Boxwright.Core`** (embedded resource), behind
  `IOsCatalogSource`. It works offline and is fully under our control; a remote manifest can
  implement the same interface later with no caller changes. First cut: a few checksum-verified
  Linux entries (Ubuntu 24.04 Desktop/Server, Debian 13 netinst); more are appended over time.
  *(Update: the remote manifest landed — see [ADR-0020](0020-remote-os-catalog.md). The bundled
  JSON is now the offline fallback behind a remote → cache → bundled source.)*
- **ISOs download to a shared cache** (`%LOCALAPPDATA%/Boxwright/ISOs` and the OS equivalents),
  referenced from the VM config by absolute path. An installer ISO is a re-downloadable shared
  asset, **not VM state**, so this stays consistent with ADR-0006 (the per-VM folder still holds
  the config + disk). Sharing dedupes multi-GB downloads — a second VM of the same OS is instant.
- **Downloads are SHA-256-verified while streaming**, to a `.part` file atomically moved into
  place only after the hash matches; a `.sha256` marker lets a later request reuse the cached file
  without re-hashing. A cancel, error, or mismatch leaves nothing behind. Provenance (source +
  size + "verified with SHA-256") is shown before the user commits; a license-gated OS (e.g. a
  Windows evaluation) shows a warning (Directives 4 & 9).
- **Download-first ordering:** the ISO is fetched before any VM is created, so a cancel/failure
  (the common case for a multi-GB download) leaves no half-created VM. VM creation then reuses the
  existing create-disk-with-rollback flow and the ISO-attach + CD-first-boot mutation.
- **Networking is behind `IHttpStreamSource`** (a thin `HttpClient` adapter) so the downloader is
  unit-tested without network. One shared `HttpClient` process-wide.

## Consequences
- **Easier:** a beginner gets a bootable VM from one screen; repeat installs are instant (cache);
  the catalog grows by editing one JSON file; the downloader is cross-platform (HttpClient /
  SHA-256 / `LocalApplicationData` paths) and fully unit-tested.
- **Harder / accepted:** the bundled catalog goes stale as distros publish point releases (a
  checksum mismatch is the honest, clean failure; a remote source later fixes freshness); large
  downloads consume disk and bandwidth; Windows and a remote manifest are not here yet.

## Alternatives considered
- **Remote-only catalog:** rejected for the first cut — adds a network dependency and a
  hosting/trust story just to *list* OSes; the interface keeps it as a future option.
- **Per-VM ISO copy:** rejected — re-downloads and duplicates multi-GB ISOs; an installer ISO is
  not VM state, so the shared cache is the better fit.
- **Size-based cache reuse:** rejected — needs byte-exact sizes; the `.sha256` marker is robust and
  needs only the verified hash.
- **Create the VM first, then download:** rejected — a cancel/failure on the long download would
  orphan a VM; download-first keeps the frequent failure path clean.
- **GPG/PGP signature verification now:** deferred — key management (multiple distro keyrings) is a
  larger surface; SHA-256 + provenance satisfies the security directive today. Windows catalog
  entries are also deferred (no stable direct ISO URL + pinned checksum), as are cloud-init /
  unattended install and virtio-win auto-attach.

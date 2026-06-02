# Boxwright.E2E — manual end-to-end harness

A small console app that exercises **real** `Boxwright.Core` integration the fake-based unit
tests can't cover. It is **not** run by `dotnet test`; run it on demand. It has three modes.

## `vm` (default) — real QEMU VM lifecycle

```
detect accelerator → create config + qcow2 disk → launch qemu-system-x86_64
→ connect QMP → pause / resume / reset → graceful + force stop
```

```
dotnet run --project tools/Boxwright.E2E
```

**Requirements:** `qemu-system-x86_64` and `qemu-img` on `PATH` (or, on Windows, the default
`C:\Program Files\qemu`). If QEMU isn't found, this mode **skips** (exit 0). A QEMU window may
appear briefly.

> This mode caught the WHPX `-cpu host` incompatibility on Windows (fixed in
> `CommandLineBuilder`). Re-run it after changing the launch path to confirm real QEMU still boots.

## `download <url> <sha256> [dir]` — real ISO download

Runs the **real** `IsoDownloader` (over `HttpClientStreamSource`) against a URL: streams it,
verifies the SHA-256, then calls again to prove the cache is reused with no re-download. Point it
at a **small** file first to sanity-check the path before trusting a multi-GB ISO.

```
# correct checksum → [PASS] downloaded + verified, then [PASS] cache reused
dotnet run --project tools/Boxwright.E2E -- download https://releases.ubuntu.com/24.04/SHA256SUMS <sha256>

# wrong checksum → [FAIL] discarded, nothing left in the cache dir
dotnet run --project tools/Boxwright.E2E -- download <url> 0000...0000
```

`dir` defaults to a fresh temp folder. **Exit code:** `0` PASS, `1` checksum/download failure.

## `hash <url>` — print a URL's SHA-256 + size

Streams a URL and prints its SHA-256 (lowercase hex) and byte size — exactly what you need to add
or re-verify an `OsCatalog.json` entry (catalog checksums go stale on point releases).

```
dotnet run --project tools/Boxwright.E2E -- hash https://releases.ubuntu.com/24.04/ubuntu-24.04.4-desktop-amd64.iso
```

**Requirements (download/hash):** outbound HTTPS. No QEMU needed.

# ADR-0027: OpenPGP signature verification for catalog downloads

- **Status:** Accepted (phase 1 — the verification primitive — implemented; download-time wiring is phase 2)
- **Date:** 2026-06-16

## Context
Catalog downloads are checksum-verified today (SHA-256, ADR-0010): the downloaded ISO must match the
`sha256` in the catalog entry. That guarantees the bytes weren't **corrupted** — but **not** that they're
**authentic**. SHA-256 protects integrity, not provenance: anyone who can change the catalog (a malicious
remote manifest, a poisoned community recipe per ADR-0026, a MITM on the manifest fetch) can swap the URL
*and* the hash together, and the download would still "verify". The roadmap flagged GPG/PGP **signature**
verification as the fast-follow to close that gap.

Distros publish a detached OpenPGP signature over their release checksums (e.g. Ubuntu's `SHA256SUMS` +
`SHA256SUMS.gpg`, signed by a long-lived release key). Verifying that signature against a key we **trust
out of band** proves the checksums — and therefore the ISO that matches them — really came from the distro.

The constraints (Directive 4: cross-platform parity; no external daemons; minimal moving parts) rule out
shelling out to `gpg` — it isn't present by default on Windows or macOS, so it would break parity and add a
discovery/version surface.

## Decision
- **Verify OpenPGP in-process with BouncyCastle** (`BouncyCastle.Cryptography`, MIT, pure-managed). It
  works identically on Windows/macOS/Linux with no external tool, matching how DiscUtils already gives us
  pure-managed ISO/FAT writing.
- **Phase 1 (this ADR): a verification primitive.** `IOpenPgpVerifier.Verify(data, signature, publicKey)`
  (`OpenPgpVerifier`) checks a **detached** signature (ASCII-armored `.asc` or binary `.gpg`) over a data
  stream against a supplied public key/keyring, returning *valid?* + the signer's key id. Malformed
  signature/key material or a key-id mismatch throws `OpenPgpException`; a well-formed signature that simply
  doesn't match is a `false` result, not an exception. This is the hard, security-critical, fully
  unit-testable core (tested by minting a throwaway PGP key, signing in-process, and checking the genuine /
  tampered / wrong-key / malformed cases — no checked-in key material, no external `gpg`).
- **Trust anchor = bundled keys.** The public keys we verify against must ship **with the app** (or be
  user-installed), never fetched over the same channel as the thing they authenticate — otherwise an
  attacker who controls the manifest controls the key too and the signature proves nothing. SHA-256 stays
  **mandatory** and unchanged; the signature is an *additional* gate, not a replacement.
- **Phase 2 (planned, not in this ADR's implementation): wire it into the download.** A catalog entry gains
  an optional signature block — the detached-signature URL (over the distro's checksums document) plus the
  id/fingerprint of the bundled key expected to have signed it. `IsoDownloader` (or a verifier around it)
  fetches the signature, verifies it against the bundled key, confirms the entry's `sha256` appears in the
  signed checksums, and only then trusts the download. Per-distro specifics (which key, which checksums
  file, filename matching) are fiddly and need real release keys bundled and an end-to-end test against a
  live download — so they are deliberately a separate change, not folded into the primitive.

## Consequences
- **Easier / safer:** provenance, not just integrity — a tampered catalog/recipe or a MITM can no longer
  substitute a malicious image that "passes", once an entry opts into signature verification with a bundled
  key. The primitive is reusable (it could also verify a signed catalog manifest itself later).
- **Harder / accepted:** a new managed dependency (BouncyCastle) in `Core`; ongoing maintenance of the
  bundled key set (release keys rotate, though slowly); per-distro wiring in phase 2 is bespoke. Until an
  entry carries signature data and a bundled key, behaviour is unchanged (SHA-256 only) — the feature is
  opt-in per entry, so it degrades gracefully.

## Alternatives considered
- **Shell out to `gpg`.** Rejected — not present by default on Windows/macOS (parity break), plus a version
  / keyring-state surface we don't control.
- **Trust the SHA-256 alone (status quo).** Rejected — it's integrity, not authenticity; the catalog itself
  is the thing an attacker would tamper with.
- **minisign / signify.** Simpler than OpenPGP, but distros don't publish minisign signatures — we'd have no
  authentic signatures to verify. OpenPGP is what the ecosystem actually signs with.

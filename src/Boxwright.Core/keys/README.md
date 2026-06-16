# Bundled OpenPGP trust anchors (ADR-0027)

Armored public keys (`<keyId>.asc`) embedded in `Boxwright.Core` and resolved by
`BundledTrustedKeyProvider`. A catalog entry's `signature.keyId` names the file
here (without the `.asc` suffix) that is expected to have signed that distro's
checksums document.

These are **trust anchors**: they ship with the app and are never fetched over
the same channel as the thing they authenticate. SHA-256 stays mandatory; the
signature is an *additional* gate.

## Status — phase 2 (mechanism + tests), real keys PENDING

This folder intentionally contains **no real distro release keys yet**. Phase 2
implements and unit-tests the wiring with a throwaway in-test key. Bundling
actual release keys (e.g. Canonical's `SHA256SUMS` signing key) and pointing a
real catalog entry at a live signature is the deferred live-verification step —
it requires real key material plus an end-to-end download to verify, so it is a
separate change.

To add a real key later:

1. Obtain the distro's release-signing public key out of band (their keyserver
   or website over a channel you trust), verify its fingerprint independently.
2. Export it armored: `gpg --armor --export <fingerprint> > <keyId>.asc`.
3. Drop the `.asc` here (it is embedded automatically by the csproj glob) and set
   the catalog entry's `signature.keyId` to `<keyId>`.

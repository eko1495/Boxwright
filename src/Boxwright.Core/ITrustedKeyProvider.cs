namespace Boxwright.Core;

/// <summary>
/// Resolves a <b>bundled</b> OpenPGP public key by the stable id a catalog entry references
/// (<see cref="OsCatalogSignature.KeyId"/>) — the trust anchor for signed catalog downloads (ADR-0027).
/// Keys must ship with the app, never be fetched over the same channel as the thing they authenticate;
/// otherwise an attacker who controls the manifest controls the key too and the signature proves nothing.
/// </summary>
/// <remarks>
/// The default <see cref="BundledTrustedKeyProvider"/> reads armored keys embedded in this assembly, so
/// they're tamper-resistant (part of the signed/installed binary). The interface is the test seam: tests
/// supply a throwaway key without checking key material into the repo.
/// </remarks>
public interface ITrustedKeyProvider
{
    /// <summary>
    /// Opens the armored or binary public key registered under <paramref name="keyId"/>, or returns null
    /// when no such key is bundled. The caller disposes the returned stream.
    /// </summary>
    Stream? OpenPublicKey(string keyId);
}

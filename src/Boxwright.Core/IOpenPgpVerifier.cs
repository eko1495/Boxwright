namespace Boxwright.Core;

/// <summary>The outcome of verifying an OpenPGP detached signature.</summary>
/// <param name="IsValid">True when the signature is cryptographically valid for the data under the supplied key.</param>
/// <param name="SignerKeyId">The 16-hex-digit key id that produced the signature (informational; shown to the user).</param>
public readonly record struct OpenPgpVerification(bool IsValid, string SignerKeyId);

/// <summary>
/// Verifies an OpenPGP <b>detached</b> signature over a data stream against a trusted public key —
/// the mechanism behind GPG-signed catalog downloads (ADR-0027). Pure-managed (BouncyCastle), so it
/// works identically on Windows, macOS, and Linux with no external <c>gpg</c>. Implemented by
/// <see cref="OpenPgpVerifier"/>.
/// </summary>
public interface IOpenPgpVerifier
{
    /// <summary>
    /// Verifies <paramref name="signature"/> (ASCII-armored <c>.asc</c> or binary <c>.gpg</c>) over
    /// <paramref name="data"/> using <paramref name="publicKey"/> (armored or binary; a key or keyring).
    /// Returns whether it is valid and which key signed it. The data stream is read to its end.
    /// </summary>
    /// <exception cref="OpenPgpException">The signature or key material is malformed, or no key matches the signature.</exception>
    OpenPgpVerification Verify(Stream data, Stream signature, Stream publicKey);
}

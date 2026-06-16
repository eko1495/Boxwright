using Org.BouncyCastle.Bcpg.OpenPgp;

namespace Boxwright.Core;

/// <summary>
/// The default <see cref="IOpenPgpVerifier"/>, over BouncyCastle's OpenPGP implementation. Reads a
/// detached signature and a public key (each armored or binary), finds the public key whose id matches
/// the signature, and checks the signature over the data.
/// </summary>
public sealed class OpenPgpVerifier : IOpenPgpVerifier
{
    /// <inheritdoc />
    public OpenPgpVerification Verify(Stream data, Stream signature, Stream publicKey)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(publicKey);

        PgpSignature sig = ReadSignature(signature);
        PgpPublicKey key = FindKey(publicKey, sig.KeyId)
            ?? throw new OpenPgpException(
                $"No public key with id {sig.KeyId:X16} in the supplied key material — it can't have signed this.");

        try
        {
            sig.InitVerify(key);
            byte[] buffer = new byte[1 << 16];
            int read;
            while ((read = data.Read(buffer, 0, buffer.Length)) > 0)
            {
                sig.Update(buffer, 0, read);
            }

            return new OpenPgpVerification(sig.Verify(), key.KeyId.ToString("X16"));
        }
        catch (PgpException ex)
        {
            throw new OpenPgpException("The OpenPGP signature could not be verified.", ex);
        }
    }

    private static PgpSignature ReadSignature(Stream signature)
    {
        try
        {
            using Stream decoded = PgpUtilities.GetDecoderStream(signature);
            var factory = new PgpObjectFactory(decoded);

            PgpObject? obj = factory.NextPgpObject();
            if (obj is PgpCompressedData compressed)
            {
                obj = new PgpObjectFactory(compressed.GetDataStream()).NextPgpObject();
            }

            if (obj is PgpSignatureList { Count: > 0 } list)
            {
                return list[0];
            }

            throw new OpenPgpException("The signature data did not contain an OpenPGP signature.");
        }
        catch (Exception ex) when (ex is IOException or PgpException)
        {
            throw new OpenPgpException("The OpenPGP signature is malformed.", ex);
        }
    }

    private static PgpPublicKey? FindKey(Stream publicKey, long keyId)
    {
        try
        {
            using Stream decoded = PgpUtilities.GetDecoderStream(publicKey);
            var bundle = new PgpPublicKeyRingBundle(decoded);
            foreach (PgpPublicKeyRing ring in bundle.GetKeyRings())
            {
                foreach (PgpPublicKey candidate in ring.GetPublicKeys())
                {
                    if (candidate.KeyId == keyId)
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or PgpException)
        {
            throw new OpenPgpException("The OpenPGP public key material is malformed.", ex);
        }
    }
}

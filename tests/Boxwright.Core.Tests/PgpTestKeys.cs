using Boxwright.Core;
using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;

namespace Boxwright.Core.Tests;

// Shared throwaway-PGP-key helpers (BouncyCastle) for the OpenPGP tests — mint a key, sign data, export
// the public key, and wrap it as an ITrustedKeyProvider. Keeps key material out of the repo: no external
// gpg, no checked-in keys. Used by both OpenPgpVerifierTests and IsoDownloaderSignatureTests.
internal static class PgpTestKeys
{
    public static PgpSecretKey NewKey()
    {
        var generator = new RsaKeyPairGenerator();
        // 1024-bit is deliberately weak — these keys live for one test, so speed beats strength.
        generator.Init(new RsaKeyGenerationParameters(BigInteger.ValueOf(0x10001), new SecureRandom(), 1024, 12));
        AsymmetricCipherKeyPair pair = generator.GenerateKeyPair();

        return new PgpSecretKey(
            PgpSignature.BinaryDocument,
            PublicKeyAlgorithmTag.RsaGeneral,
            pair.Public,
            pair.Private,
            DateTime.UnixEpoch,
            "Boxwright Test <test@example.invalid>",
            SymmetricKeyAlgorithmTag.Null,
            [],
            null,
            null,
            new SecureRandom());
    }

    public static byte[] Sign(PgpSecretKey key, byte[] data)
    {
        PgpPrivateKey privateKey = key.ExtractPrivateKey([]);
        var generator = new PgpSignatureGenerator(key.PublicKey.Algorithm, HashAlgorithmTag.Sha256);
        generator.InitSign(PgpSignature.BinaryDocument, privateKey);
        generator.Update(data);
        PgpSignature signature = generator.Generate();

        using var memory = new MemoryStream();
        using (var armored = new ArmoredOutputStream(memory))
        {
            signature.Encode(armored);
        }

        return memory.ToArray();
    }

    public static byte[] ExportPublicKey(PgpSecretKey key)
    {
        using var memory = new MemoryStream();
        using (var armored = new ArmoredOutputStream(memory))
        {
            key.PublicKey.Encode(armored);
        }

        return memory.ToArray();
    }

    // An ITrustedKeyProvider that returns the exported public key for one id and null for anything else —
    // the test seam ADR-0027 specifies so tests supply a key without a real bundled-key store.
    public static ITrustedKeyProvider NewProvider(PgpSecretKey key, string registeredId = "test-distro") =>
        new SingleKeyProvider(registeredId, ExportPublicKey(key));

    private sealed class SingleKeyProvider(string keyId, byte[] armoredPublicKey) : ITrustedKeyProvider
    {
        public Stream? OpenPublicKey(string id) =>
            string.Equals(id, keyId, StringComparison.Ordinal)
                ? new MemoryStream(armoredPublicKey, writable: false)
                : null;
    }
}

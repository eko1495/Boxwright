using System.Text;
using Boxwright.Core;
using Org.BouncyCastle.Bcpg.OpenPgp;
using Xunit;

namespace Boxwright.Core.Tests;

// OpenPgpVerifier checks a detached OpenPGP signature against a trusted public key. Each test mints a
// throwaway PGP key (via PgpTestKeys), signs in-process, and verifies — no external gpg, no checked-in
// key material.
public sealed class OpenPgpVerifierTests
{
    private static readonly byte[] Data = Encoding.UTF8.GetBytes("ubuntu-24.04.iso  sha256  deadbeef\n");

    [Fact]
    public void Verify_ReturnsValid_ForAGenuineSignature()
    {
        PgpSecretKey key = NewKey();
        byte[] signature = Sign(key, Data);
        byte[] publicKey = ExportPublicKey(key);

        OpenPgpVerification result = new OpenPgpVerifier().Verify(new MemoryStream(Data), new MemoryStream(signature), new MemoryStream(publicKey));

        Assert.True(result.IsValid);
        Assert.Equal(key.KeyId.ToString("X16"), result.SignerKeyId);
    }

    [Fact]
    public void Verify_ReturnsInvalid_WhenTheDataWasTampered()
    {
        PgpSecretKey key = NewKey();
        byte[] signature = Sign(key, Data);
        byte[] publicKey = ExportPublicKey(key);

        byte[] tampered = (byte[])Data.Clone();
        tampered[0] ^= 0xFF; // flip a byte — the signature no longer matches

        OpenPgpVerification result = new OpenPgpVerifier().Verify(new MemoryStream(tampered), new MemoryStream(signature), new MemoryStream(publicKey));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Verify_Throws_WhenTheKeyDidNotSignTheData()
    {
        byte[] signature = Sign(NewKey(), Data);
        byte[] otherKey = ExportPublicKey(NewKey()); // a different key — no id match

        OpenPgpException ex = Assert.Throws<OpenPgpException>(() =>
            new OpenPgpVerifier().Verify(new MemoryStream(Data), new MemoryStream(signature), new MemoryStream(otherKey)));

        Assert.Contains("No public key", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_Throws_OnMalformedSignature()
    {
        byte[] publicKey = ExportPublicKey(NewKey());

        Assert.Throws<OpenPgpException>(() =>
            new OpenPgpVerifier().Verify(new MemoryStream(Data), new MemoryStream("not a signature"u8.ToArray()), new MemoryStream(publicKey)));
    }

    // ---- shared throwaway-key helpers (PgpTestKeys) ----

    private static PgpSecretKey NewKey() => PgpTestKeys.NewKey();

    private static byte[] Sign(PgpSecretKey key, byte[] data) => PgpTestKeys.Sign(key, data);

    private static byte[] ExportPublicKey(PgpSecretKey key) => PgpTestKeys.ExportPublicKey(key);
}

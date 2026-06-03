using System.Security.Cryptography;
using System.Text;

namespace Boxwright.Core;

/// <summary>
/// The SHA-512 variant of the Unix <c>crypt(3)</c> password hash — the <c>$6$</c> scheme from Ulrich
/// Drepper's specification — implemented in pure managed code (the .NET BCL has no <c>crypt(3)</c>).
/// Ubuntu autoinstall's <c>identity.password</c> requires such a hash, and we must produce it
/// cross-platform so a seed built on Windows logs in on the Linux guest. Verified against the
/// published test vectors (see <c>Sha512CryptTests</c>).
/// </summary>
public static class Sha512Crypt
{
    private const int Rounds = 5000; // the scheme's default round count
    private const int BlockSize = 64; // SHA-512 digest length, in bytes
    private const string Alphabet = "./0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    /// <summary>Hashes <paramref name="password"/> with a fresh random 16-character salt.</summary>
    public static string Hash(string password) => Hash(password, RandomSalt());

    /// <summary>
    /// Hashes <paramref name="password"/> with the given <paramref name="salt"/> (capped at 16
    /// characters) at the default round count, returning the full <c>$6$salt$digest</c> string.
    /// </summary>
    public static string Hash(string password, string salt)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(salt);

        if (salt.Length > 16)
        {
            salt = salt[..16];
        }

        byte[] key = Encoding.UTF8.GetBytes(password);
        byte[] saltBytes = Encoding.UTF8.GetBytes(salt);
        byte[] digest = ComputeDigest(key, saltBytes);
        return $"$6${salt}${Encode(digest)}";
    }

    // A direct transcription of glibc's sha512-crypt.c (default rounds, no "rounds=" prefix).
    private static byte[] ComputeDigest(byte[] key, byte[] salt)
    {
        // Digest B = SHA512(key + salt + key).
        byte[] b = Sha512(key, salt, key);

        // Digest A = SHA512(key + salt + B·|key| + (per bit of |key|: B if set else key)).
        using IncrementalHash a = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
        a.AppendData(key);
        a.AppendData(salt);
        AppendRepeated(a, b, key.Length);
        for (int cnt = key.Length; cnt > 0; cnt >>= 1)
        {
            a.AppendData((cnt & 1) != 0 ? b : key);
        }

        byte[] digestA = a.GetHashAndReset();

        // P = |key| bytes of SHA512(key·|key|), repeated.
        using IncrementalHash dp = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
        for (int i = 0; i < key.Length; i++)
        {
            dp.AppendData(key);
        }

        byte[] p = Produce(dp.GetHashAndReset(), key.Length);

        // S = |salt| bytes of SHA512(salt·(16 + digestA[0])), repeated.
        using IncrementalHash ds = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
        int saltRounds = 16 + digestA[0];
        for (int i = 0; i < saltRounds; i++)
        {
            ds.AppendData(salt);
        }

        byte[] s = Produce(ds.GetHashAndReset(), salt.Length);

        // Round mixing: 5000 iterations alternating P/C, S, P per the (1,3,7) schedule.
        byte[] c = digestA;
        for (int i = 0; i < Rounds; i++)
        {
            using IncrementalHash ctx = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
            ctx.AppendData((i & 1) != 0 ? p : c);
            if (i % 3 != 0)
            {
                ctx.AppendData(s);
            }

            if (i % 7 != 0)
            {
                ctx.AppendData(p);
            }

            ctx.AppendData((i & 1) != 0 ? c : p);
            c = ctx.GetHashAndReset();
        }

        return c;
    }

    private static byte[] Sha512(params byte[][] chunks)
    {
        using IncrementalHash h = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
        foreach (byte[] chunk in chunks)
        {
            h.AppendData(chunk);
        }

        return h.GetHashAndReset();
    }

    // Append the first <paramref name="length"/> bytes of the (repeating) 64-byte block.
    private static void AppendRepeated(IncrementalHash hash, byte[] block, int length)
    {
        int remaining = length;
        while (remaining > BlockSize)
        {
            hash.AppendData(block);
            remaining -= BlockSize;
        }

        hash.AppendData(block.AsSpan(0, remaining));
    }

    // Build a byte sequence of the given length by repeating the 64-byte block.
    private static byte[] Produce(byte[] block, int length)
    {
        byte[] result = new byte[length];
        for (int i = 0; i < length; i++)
        {
            result[i] = block[i % BlockSize];
        }

        return result;
    }

    private static string RandomSalt()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        Span<char> chars = stackalloc char[16];
        for (int i = 0; i < chars.Length; i++)
        {
            chars[i] = Alphabet[bytes[i] & 0x3f];
        }

        return new string(chars);
    }

    // The custom base64 of the 64-byte digest: glibc's byte permutation, low 6 bits first.
    private static string Encode(byte[] c)
    {
        var sb = new StringBuilder(86);
        Group(sb, c[0], c[21], c[42]);
        Group(sb, c[22], c[43], c[1]);
        Group(sb, c[44], c[2], c[23]);
        Group(sb, c[3], c[24], c[45]);
        Group(sb, c[25], c[46], c[4]);
        Group(sb, c[47], c[5], c[26]);
        Group(sb, c[6], c[27], c[48]);
        Group(sb, c[28], c[49], c[7]);
        Group(sb, c[50], c[8], c[29]);
        Group(sb, c[9], c[30], c[51]);
        Group(sb, c[31], c[52], c[10]);
        Group(sb, c[53], c[11], c[32]);
        Group(sb, c[12], c[33], c[54]);
        Group(sb, c[34], c[55], c[13]);
        Group(sb, c[56], c[14], c[35]);
        Group(sb, c[15], c[36], c[57]);
        Group(sb, c[37], c[58], c[16]);
        Group(sb, c[59], c[17], c[38]);
        Group(sb, c[18], c[39], c[60]);
        Group(sb, c[40], c[61], c[19]);
        Group(sb, c[62], c[20], c[41]);
        Emit(sb, 0, 0, c[63], 2);
        return sb.ToString();
    }

    private static void Group(StringBuilder sb, byte b2, byte b1, byte b0) => Emit(sb, b2, b1, b0, 4);

    private static void Emit(StringBuilder sb, byte b2, byte b1, byte b0, int count)
    {
        int w = (b2 << 16) | (b1 << 8) | b0;
        for (int i = 0; i < count; i++)
        {
            sb.Append(Alphabet[w & 0x3f]);
            w >>= 6;
        }
    }
}

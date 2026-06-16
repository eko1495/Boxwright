using System.Text;

namespace Boxwright.Core;

/// <summary>
/// Parses a distro <c>SHA256SUMS</c>-style checksums document — the thing an OpenPGP signature vouches
/// for (ADR-0027). Each non-blank line is <c>&lt;hex-hash&gt;  &lt;filename&gt;</c>; a leading <c>*</c> on
/// the filename (GNU coreutils "binary mode") and surrounding whitespace are ignored. Matching is only
/// ever done on a document whose signature has <b>already</b> verified, so this is a pure data check.
/// </summary>
internal static class SignedChecksums
{
    /// <summary>
    /// True when <paramref name="document"/> lists <paramref name="expectedHash"/> against
    /// <paramref name="fileName"/> — both the hash and the filename must match on the same line, so one
    /// image's hash can't be accepted under another image's name. Hash compare is case-insensitive hex;
    /// filename compare is ordinal (filenames are case-sensitive on the distros we target).
    /// </summary>
    public static bool Contains(ReadOnlySpan<byte> document, string expectedHash, string fileName)
    {
        if (string.IsNullOrWhiteSpace(expectedHash) || string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        // Checksums files are ASCII/UTF-8; decode the whole thing (these documents are small — a few KiB).
        string text = Encoding.UTF8.GetString(document);
        foreach (ReadOnlySpan<char> rawLine in text.AsSpan().EnumerateLines())
        {
            ReadOnlySpan<char> line = rawLine.Trim();
            if (line.IsEmpty)
            {
                continue;
            }

            int split = IndexOfWhitespace(line);
            if (split <= 0)
            {
                continue;
            }

            ReadOnlySpan<char> hash = line[..split];
            ReadOnlySpan<char> name = line[split..].TrimStart();

            // A "binary mode" marker ("*name") is part of coreutils' format, not the name itself.
            if (!name.IsEmpty && name[0] == '*')
            {
                name = name[1..];
            }

            if (hash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase)
                && name.Equals(fileName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static int IndexOfWhitespace(ReadOnlySpan<char> line)
    {
        for (int i = 0; i < line.Length; i++)
        {
            if (char.IsWhiteSpace(line[i]))
            {
                return i;
            }
        }

        return -1;
    }
}

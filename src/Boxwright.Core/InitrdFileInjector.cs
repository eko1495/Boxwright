using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Boxwright.Core;

/// <summary>
/// Injects a file into an installer initrd by <b>appending</b> a gzipped single-file cpio "newc"
/// archive. A Linux initramfs is a concatenation of (independently-compressed) cpio segments, so the
/// kernel reads the appended segment too and its files overlay the originals — no read-modify-write of
/// the existing initrd, and the base initrd's own compression (gzip/xz/zstd) doesn't matter. Used to
/// drop an unattended-install answer file at the initramfs root: a debian-installer <c>preseed.cfg</c>
/// (auto-read by d-i) or a Fedora Anaconda <c>ks.cfg</c> (booted with <c>inst.ks=file:/ks.cfg</c>).
/// Pure managed (<see cref="GZipStream"/> + a hand-written cpio header) — identical on Windows, macOS,
/// and Linux.
/// </summary>
public static class InitrdFileInjector
{
    private const string Magic = "070701";              // cpio "newc" magic
    private const int RegularFileMode = 0x81A4;          // S_IFREG | 0644
    private const string Trailer = "TRAILER!!!";         // marks the end of a cpio archive

    /// <summary>
    /// Appends <paramref name="content"/> as <paramref name="fileName"/> (at the initramfs root) to the
    /// initrd at <paramref name="initrdPath"/>.
    /// </summary>
    public static void Append(string initrdPath, string fileName, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(initrdPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(content);

        byte[] segment = GzipCpio(fileName, Encoding.UTF8.GetBytes(content));
        using var fs = new FileStream(initrdPath, FileMode.Append, FileAccess.Write, FileShare.None);
        fs.Write(segment);
    }

    // A one-file cpio "newc" archive (the file + the TRAILER entry), gzip-compressed.
    private static byte[] GzipCpio(string fileName, byte[] data)
    {
        using var raw = new MemoryStream();
        WriteEntry(raw, fileName, data, RegularFileMode);
        WriteEntry(raw, Trailer, [], mode: 0);

        using var compressed = new MemoryStream();
        using (var gz = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            gz.Write(raw.ToArray());
        }

        return compressed.ToArray();
    }

    private static void WriteEntry(Stream stream, string name, byte[] data, int mode)
    {
        byte[] nameBytes = Encoding.ASCII.GetBytes(name);
        int nameSize = nameBytes.Length + 1; // includes the trailing NUL

        var header = new StringBuilder(110);
        header.Append(Magic);
        AppendHex(header, 0);          // c_ino
        AppendHex(header, mode);       // c_mode
        AppendHex(header, 0);          // c_uid
        AppendHex(header, 0);          // c_gid
        AppendHex(header, 1);          // c_nlink
        AppendHex(header, 0);          // c_mtime
        AppendHex(header, data.Length);// c_filesize
        AppendHex(header, 0);          // c_devmajor
        AppendHex(header, 0);          // c_devminor
        AppendHex(header, 0);          // c_rdevmajor
        AppendHex(header, 0);          // c_rdevminor
        AppendHex(header, nameSize);   // c_namesize
        AppendHex(header, 0);          // c_check (0 for "newc")

        byte[] headerBytes = Encoding.ASCII.GetBytes(header.ToString()); // exactly 110 bytes
        stream.Write(headerBytes);
        stream.Write(nameBytes);
        stream.WriteByte(0); // NUL terminator
        Pad(stream, headerBytes.Length + nameSize);

        stream.Write(data);
        Pad(stream, data.Length);
    }

    // newc fields are 8-digit lowercase hex; archive members are padded to a 4-byte boundary.
    private static void AppendHex(StringBuilder sb, int value) => sb.Append(value.ToString("x8", CultureInfo.InvariantCulture));

    private static void Pad(Stream stream, int writtenLength)
    {
        int pad = (4 - (writtenLength % 4)) % 4;
        for (int i = 0; i < pad; i++)
        {
            stream.WriteByte(0);
        }
    }
}

using DiscUtils.Iso9660;

namespace Boxwright.Core;

/// <summary>
/// A tiny read-only wrapper over an installer ISO: opens it with DiscUtils' <see cref="CDReader"/>
/// (Joliet, pure managed — no external tool) and copies/reads files out of it. Shared by the
/// per-family install-media extractors (Ubuntu casper, Debian <c>install.amd</c>, …) so the
/// open + file-copy mechanics live in one place. Dispose to release the underlying file handle.
/// </summary>
internal sealed class IsoMedia : IDisposable
{
    private readonly FileStream _stream;
    private readonly CDReader _reader;

    private IsoMedia(FileStream stream, CDReader reader)
    {
        _stream = stream;
        _reader = reader;
    }

    /// <summary>Opens <paramref name="isoPath"/> for reading.</summary>
    /// <exception cref="InstallMediaException">The file can't be opened or isn't a readable ISO9660 image.</exception>
    public static IsoMedia Open(string isoPath)
    {
        FileStream stream;
        try
        {
            stream = new FileStream(isoPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InstallMediaException($"Couldn't open the installer ISO '{Path.GetFileName(isoPath)}': {ex.Message}", ex);
        }

        try
        {
            return new IsoMedia(stream, new CDReader(stream, joliet: true));
        }
        catch (Exception ex)
        {
            // Any parse failure means it isn't a usable ISO9660 image.
            stream.Dispose();
            throw new InstallMediaException($"'{Path.GetFileName(isoPath)}' is not a readable ISO9660 image: {ex.Message}", ex);
        }
    }

    /// <summary>The ISO9660 volume label (e.g. for a Fedora <c>inst.stage2=hd:LABEL=…</c> boot arg).</summary>
    public string VolumeLabel => _reader.VolumeLabel ?? string.Empty;

    /// <summary>Whether a file exists at <paramref name="isoPath"/> (DiscUtils uses backslash separators).</summary>
    public bool FileExists(string isoPath) => _reader.FileExists(isoPath);

    /// <summary>Copies a file out of the ISO to <paramref name="destination"/> on the host.</summary>
    public void CopyFile(string isoPath, string destination)
    {
        using Stream source = _reader.OpenFile(isoPath, FileMode.Open, FileAccess.Read);
        using var dest = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        source.CopyTo(dest);
    }

    /// <summary>Reads a text file out of the ISO.</summary>
    public string ReadText(string isoPath)
    {
        using Stream stream = _reader.OpenFile(isoPath, FileMode.Open, FileAccess.Read);
        using var text = new StreamReader(stream);
        return text.ReadToEnd();
    }

    public void Dispose()
    {
        _reader.Dispose();
        _stream.Dispose();
    }
}

namespace Boxwright.Core;

/// <summary>Thrown when an ISO download fails — a network error, a non-success HTTP status, or a checksum mismatch.</summary>
public sealed class DownloadException : Exception
{
    /// <summary>Creates an exception with no message.</summary>
    public DownloadException()
    {
    }

    /// <summary>Creates an exception with the given <paramref name="message"/>.</summary>
    public DownloadException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with the given <paramref name="message"/> and <paramref name="innerException"/>.</summary>
    public DownloadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

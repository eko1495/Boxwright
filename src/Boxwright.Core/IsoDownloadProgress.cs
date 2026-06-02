namespace Boxwright.Core;

/// <summary>Progress of an ISO download: bytes received so far, and the total when it is known.</summary>
public readonly record struct IsoDownloadProgress(long BytesReceived, long? TotalBytes)
{
    /// <summary>Completion as a 0–100 percentage, or null when the total size is unknown.</summary>
    public double? Percent => TotalBytes is > 0
        ? Math.Min(100d, 100d * BytesReceived / TotalBytes.Value)
        : null;
}

using System.Text.Json.Serialization;

namespace Boxwright.Core;

/// <summary>
/// A disk image's metadata, parsed from <c>qemu-img info --output=json</c>.
/// </summary>
public sealed record DiskInfo
{
    /// <summary>The image file name.</summary>
    [JsonPropertyName("filename")]
    public string? Filename { get; init; }

    /// <summary>The image format, e.g. <c>qcow2</c>.</summary>
    [JsonPropertyName("format")]
    public string? Format { get; init; }

    /// <summary>The logical (virtual) size in bytes.</summary>
    [JsonPropertyName("virtual-size")]
    public long VirtualSize { get; init; }

    /// <summary>The on-disk (actual) size in bytes.</summary>
    [JsonPropertyName("actual-size")]
    public long ActualSize { get; init; }
}

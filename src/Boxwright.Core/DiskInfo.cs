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

    /// <summary>Internal snapshots stored in the image (empty when there are none).</summary>
    [JsonPropertyName("snapshots")]
    public IReadOnlyList<VmSnapshot> Snapshots { get; init; } = [];

    /// <summary>The image's immediate backing file as recorded in the image (relative or absolute), or null when it has none.</summary>
    [JsonPropertyName("backing-filename")]
    public string? BackingFilename { get; init; }

    /// <summary>The image's immediate backing file resolved to an absolute path, or null when it has none. Preferred for chain comparisons.</summary>
    [JsonPropertyName("full-backing-filename")]
    public string? FullBackingFilename { get; init; }
}

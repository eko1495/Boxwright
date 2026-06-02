namespace Boxwright.Core;

/// <summary>
/// The OS catalog document: a versioned list of installable OS images. Deserialized
/// from the curated <c>OsCatalog.json</c> bundled with the app (see
/// <see cref="BundledOsCatalogSource"/>) via <see cref="OsCatalogJson"/>.
/// </summary>
public sealed record OsCatalogDocument
{
    /// <summary>The catalog schema version this build understands.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Schema version of the document (must equal <see cref="CurrentSchemaVersion"/>).</summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>The catalog entries.</summary>
    public IReadOnlyList<OsCatalogEntry> Entries { get; init; } = [];
}

/// <summary>
/// One installable OS image: where to download it, how to verify it, its provenance,
/// and the recommended VM specs the catalog prefills for it.
/// </summary>
public sealed record OsCatalogEntry
{
    /// <summary>Stable identifier, e.g. <c>ubuntu-24.04-desktop</c>.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Display name, e.g. <c>Ubuntu Desktop</c>.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Human version label, e.g. <c>24.04.2 LTS</c>.</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>Guest architecture (e.g. <c>x86_64</c>).</summary>
    public string Arch { get; init; } = "x86_64";

    /// <summary>Direct download URL of the installer ISO (https).</summary>
    public Uri IsoUrl { get; init; } = null!;

    /// <summary>Expected SHA-256 of the ISO, lowercase hex.</summary>
    public string Sha256 { get; init; } = string.Empty;

    /// <summary>Expected download size in bytes (also used for cache-hit detection).</summary>
    public long SizeBytes { get; init; }

    /// <summary>Recommended VM specs for this OS.</summary>
    public OsRecommendedSpec Recommended { get; init; } = new();

    /// <summary>Human-readable provenance, e.g. <c>Canonical · releases.ubuntu.com</c>.</summary>
    public string SourceName { get; init; } = string.Empty;

    /// <summary>True when the OS needs a license the user must supply (e.g. a Windows evaluation).</summary>
    public bool RequiresLicense { get; init; }

    /// <summary>Optional note shown to the user (e.g. evaluation terms or install hints).</summary>
    public string? Notes { get; init; }
}

/// <summary>Recommended VM specs the catalog prefills for an <see cref="OsCatalogEntry"/>.</summary>
public sealed record OsRecommendedSpec
{
    /// <summary>Recommended memory in MiB.</summary>
    public int MemoryMiB { get; init; } = 2048;

    /// <summary>Recommended CPU cores.</summary>
    public int CpuCores { get; init; } = 2;

    /// <summary>Recommended primary disk size in GiB.</summary>
    public int DiskGiB { get; init; } = 20;

    /// <summary>Recommended firmware (<c>bios</c> or <c>uefi</c>).</summary>
    public string Firmware { get; init; } = "uefi";
}

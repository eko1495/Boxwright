using System.Text.Json;

namespace Boxwright.Core;

/// <summary>
/// Loads and saves the <see cref="LiveSnapshotManifest"/> sidecar (<c>live-snapshots.json</c>). Reading is
/// lenient and a missing file is treated as an empty manifest; writing is indented camelCase. Mirrors
/// <see cref="VmConfigJson"/>.
/// </summary>
public static class LiveSnapshotManifestJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Serializes a manifest to indented JSON.</summary>
    public static string Serialize(LiveSnapshotManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return JsonSerializer.Serialize(manifest, Options);
    }

    /// <summary>Deserializes a manifest, validating its schema version.</summary>
    /// <exception cref="DiskException">The JSON is malformed or its schema version is unsupported.</exception>
    public static LiveSnapshotManifest Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        LiveSnapshotManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<LiveSnapshotManifest>(json, Options)
                ?? throw new DiskException("The live-snapshot manifest JSON deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new DiskException("The live-snapshot manifest JSON is malformed.", ex);
        }

        if (manifest.SchemaVersion != LiveSnapshotManifest.CurrentSchemaVersion)
        {
            throw new DiskException(
                $"Unsupported live-snapshot manifest schemaVersion {manifest.SchemaVersion}; this build supports version {LiveSnapshotManifest.CurrentSchemaVersion}.");
        }

        return manifest;
    }

    /// <summary>Loads the manifest from a file, returning an empty manifest when the file does not exist.</summary>
    /// <exception cref="DiskException">The file's JSON is malformed or its schema version is unsupported.</exception>
    public static async Task<LiveSnapshotManifest> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            return new LiveSnapshotManifest();
        }

        string json = await File.ReadAllTextAsync(path, cancellationToken);
        return Deserialize(json);
    }

    /// <summary>Saves the manifest to a file (overwriting any existing file).</summary>
    public static async Task SaveAsync(string path, LiveSnapshotManifest manifest, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(manifest);
        await File.WriteAllTextAsync(path, Serialize(manifest), cancellationToken);
    }
}

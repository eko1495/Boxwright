using System.Text.Json;

namespace Boxwright.Core;

/// <summary>
/// Loads and saves <see cref="VmConfig"/> as human-readable JSON. Reading is
/// lenient (comments and trailing commas are allowed, so configs stay
/// hand-editable per ADR-0006); writing is indented camelCase.
/// </summary>
public static class VmConfigJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Serializes a config to indented JSON.</summary>
    public static string Serialize(VmConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return JsonSerializer.Serialize(config, Options);
    }

    /// <summary>
    /// Deserializes a config from JSON, validating that its <c>schemaVersion</c>
    /// is supported by this build.
    /// </summary>
    /// <exception cref="VmConfigException">The JSON is malformed or its schema version is unsupported.</exception>
    public static VmConfig Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        VmConfig config;
        try
        {
            config = JsonSerializer.Deserialize<VmConfig>(json, Options)
                ?? throw new VmConfigException("The VM config JSON deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new VmConfigException("The VM config JSON is malformed.", ex);
        }

        if (config.SchemaVersion != VmConfig.CurrentSchemaVersion)
        {
            throw new VmConfigException(
                $"Unsupported VM config schemaVersion {config.SchemaVersion}; this build supports version {VmConfig.CurrentSchemaVersion}.");
        }

        return config;
    }

    /// <summary>Loads a config from a JSON file.</summary>
    /// <exception cref="VmConfigException">The file's JSON is malformed or its schema version is unsupported.</exception>
    public static async Task<VmConfig> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string json = await File.ReadAllTextAsync(path, cancellationToken);
        return Deserialize(json);
    }

    /// <summary>Saves a config to a JSON file (overwriting any existing file).</summary>
    public static async Task SaveAsync(string path, VmConfig config, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(config);
        await File.WriteAllTextAsync(path, Serialize(config), cancellationToken);
    }
}

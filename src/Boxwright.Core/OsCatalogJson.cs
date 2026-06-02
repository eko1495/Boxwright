using System.Text.Json;

namespace Boxwright.Core;

/// <summary>
/// Parses the OS catalog JSON. Reading is lenient (comments and trailing commas are
/// allowed, so the bundled catalog stays hand-editable), and the <c>schemaVersion</c>
/// is validated. Mirrors <see cref="VmConfigJson"/>.
/// </summary>
public static class OsCatalogJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Deserializes a catalog document, validating its schema version.</summary>
    /// <exception cref="OsCatalogException">The JSON is malformed or its schema version is unsupported.</exception>
    public static OsCatalogDocument Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        OsCatalogDocument document;
        try
        {
            document = JsonSerializer.Deserialize<OsCatalogDocument>(json, Options)
                ?? throw new OsCatalogException("The OS catalog JSON deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new OsCatalogException("The OS catalog JSON is malformed.", ex);
        }

        if (document.SchemaVersion != OsCatalogDocument.CurrentSchemaVersion)
        {
            throw new OsCatalogException(
                $"Unsupported OS catalog schemaVersion {document.SchemaVersion}; this build supports version {OsCatalogDocument.CurrentSchemaVersion}.");
        }

        return document;
    }
}

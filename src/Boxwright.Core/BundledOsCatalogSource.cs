namespace Boxwright.Core;

/// <summary>
/// Loads the OS catalog from the curated <c>OsCatalog.json</c> embedded in this assembly.
/// Read-only and stateless, so it is safe to share as a singleton.
/// </summary>
public sealed class BundledOsCatalogSource : IOsCatalogSource
{
    // Logical manifest name = "<RootNamespace>.<file>" = "Boxwright.Core.OsCatalog.json".
    private const string ResourceName = "Boxwright.Core.OsCatalog.json";

    /// <inheritdoc />
    public async Task<IReadOnlyList<OsCatalogEntry>> GetEntriesAsync(CancellationToken cancellationToken = default)
    {
        await using Stream stream = typeof(BundledOsCatalogSource).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new OsCatalogException($"The bundled OS catalog resource '{ResourceName}' was not found.");

        using var reader = new StreamReader(stream);
        string json = await reader.ReadToEndAsync(cancellationToken);
        return OsCatalogJson.Deserialize(json).Entries;
    }
}

using Microsoft.Extensions.Logging;

namespace Boxwright.Core;

/// <summary>
/// Loads user/community "recipes" — OS catalog documents dropped as <c>*.json</c> files in a local
/// folder — so an OS can be added without recompiling or touching the bundled/remote catalog (ADR-0026).
/// Each file is the same shape as the bundled catalog (a versioned <see cref="OsCatalogDocument"/>), so
/// existing entries are copy-paste recipes. A malformed file is skipped (logged), never breaking the rest.
/// </summary>
public sealed class LocalRecipeCatalogSource : IOsCatalogSource
{
    private readonly string _directory;
    private readonly ILogger? _logger;

    /// <summary>Creates a source over <paramref name="directory"/> (the recipes folder).</summary>
    public LocalRecipeCatalogSource(string directory, ILogger<LocalRecipeCatalogSource>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
        _logger = logger;
    }

    /// <summary>
    /// The default recipes folder (e.g. <c>%LOCALAPPDATA%\Boxwright\recipes</c> on Windows,
    /// <c>~/.local/share/Boxwright/recipes</c> on Linux) — beside the VMs root.
    /// </summary>
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Boxwright", "recipes");

    /// <summary>The folder this source reads recipes from.</summary>
    public string Directory => _directory;

    /// <inheritdoc />
    public async Task<IReadOnlyList<OsCatalogEntry>> GetEntriesAsync(CancellationToken cancellationToken = default)
    {
        if (!System.IO.Directory.Exists(_directory))
        {
            return [];
        }

        var entries = new List<OsCatalogEntry>();
        foreach (string file in System.IO.Directory.EnumerateFiles(_directory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                string json = await File.ReadAllTextAsync(file, cancellationToken);
                entries.AddRange(OsCatalogJson.Deserialize(json).Entries);
            }
            catch (Exception ex) when (ex is OsCatalogException or IOException or UnauthorizedAccessException)
            {
                // One bad recipe must not sink the whole catalog — skip it, but say which and why.
                _logger?.LogWarning(ex, "Skipping recipe '{File}': {Message}", file, ex.Message);
            }
        }

        return entries;
    }
}

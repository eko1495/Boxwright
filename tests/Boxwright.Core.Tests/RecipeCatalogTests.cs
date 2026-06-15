using Xunit;

namespace Boxwright.Core.Tests;

public sealed class RecipeCatalogTests : IDisposable
{
    private readonly string _dir;

    public RecipeCatalogTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"boxwright-recipes-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void WriteRecipe(string file, params string[] ids)
    {
        string entries = string.Join(",", ids.Select(id =>
            $$"""{ "id": "{{id}}", "name": "{{id}}", "version": "1", "arch": "x86_64", "isoUrl": "https://example.invalid/{{id}}.iso", "sha256": "a", "sourceName": "s" }"""));
        File.WriteAllText(Path.Combine(_dir, file), $$"""{ "schemaVersion": 1, "entries": [ {{entries}} ] }""");
    }

    // ---- LocalRecipeCatalogSource ----

    [Fact]
    public async Task Local_ReadsEntriesFromRecipeFiles()
    {
        WriteRecipe("a.json", "alpine-3.21");
        WriteRecipe("b.json", "void-glibc", "void-musl");
        var source = new LocalRecipeCatalogSource(_dir);

        IReadOnlyList<OsCatalogEntry> entries = await source.GetEntriesAsync();

        Assert.Equal(3, entries.Count);
        Assert.Contains(entries, e => e.Id == "alpine-3.21");
        Assert.Contains(entries, e => e.Id == "void-musl");
    }

    [Fact]
    public async Task Local_SkipsAMalformedRecipe_KeepsTheRest()
    {
        WriteRecipe("good.json", "alpine-3.21");
        File.WriteAllText(Path.Combine(_dir, "bad.json"), "{ not json");

        IReadOnlyList<OsCatalogEntry> entries = await new LocalRecipeCatalogSource(_dir).GetEntriesAsync();

        OsCatalogEntry only = Assert.Single(entries);
        Assert.Equal("alpine-3.21", only.Id);
    }

    [Fact]
    public async Task Local_MissingDirectory_ReturnsEmpty()
    {
        var source = new LocalRecipeCatalogSource(Path.Combine(_dir, "nope"));

        Assert.Empty(await source.GetEntriesAsync());
    }

    // ---- CompositeOsCatalogSource ----

    [Fact]
    public async Task Composite_MergesSources_LaterWinsById_PreservesOrder()
    {
        var baseSource = new FixedSource(Entry("ubuntu"), Entry("debian", "Debian Base"));
        var overlay = new FixedSource(Entry("debian", "Debian Override"), Entry("alpine"));
        var composite = new CompositeOsCatalogSource([baseSource, overlay]);

        IReadOnlyList<OsCatalogEntry> entries = await composite.GetEntriesAsync();

        Assert.Equal(["ubuntu", "debian", "alpine"], entries.Select(e => e.Id)); // first-seen order
        Assert.Equal("Debian Override", entries.Single(e => e.Id == "debian").Name); // overlay wins
    }

    [Fact]
    public async Task Composite_SkipsAThrowingSource()
    {
        var composite = new CompositeOsCatalogSource([new ThrowingSource(), new FixedSource(Entry("alpine"))]);

        OsCatalogEntry only = Assert.Single(await composite.GetEntriesAsync());
        Assert.Equal("alpine", only.Id);
    }

    private static OsCatalogEntry Entry(string id, string? name = null) => new()
    {
        Id = id,
        Name = name ?? id,
        Version = "1",
        Arch = "x86_64",
        IsoUrl = new Uri($"https://example.invalid/{id}.iso"),
    };

    private sealed class FixedSource(params OsCatalogEntry[] entries) : IOsCatalogSource
    {
        public Task<IReadOnlyList<OsCatalogEntry>> GetEntriesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OsCatalogEntry>>(entries);
    }

    private sealed class ThrowingSource : IOsCatalogSource
    {
        public Task<IReadOnlyList<OsCatalogEntry>> GetEntriesAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<IReadOnlyList<OsCatalogEntry>>(new OsCatalogException("source unavailable"));
    }
}

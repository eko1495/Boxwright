using Boxwright.Core;
using Xunit;

namespace Boxwright.Core.Tests;

public sealed class OsCatalogTests
{
    [Fact]
    public async Task BundledCatalog_LoadsAndIsWellFormed()
    {
        var source = new BundledOsCatalogSource();

        IReadOnlyList<OsCatalogEntry> entries = await source.GetEntriesAsync();

        Assert.NotEmpty(entries);
        foreach (OsCatalogEntry e in entries)
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Id));
            Assert.False(string.IsNullOrWhiteSpace(e.Name));
            Assert.NotNull(e.IsoUrl);
            Assert.True(e.IsoUrl.IsAbsoluteUri);
            Assert.Equal(Uri.UriSchemeHttps, e.IsoUrl.Scheme);
            Assert.Matches("^[0-9a-f]{64}$", e.Sha256); // lowercase hex SHA-256
            Assert.True(e.SizeBytes > 0);
            Assert.True(e.Recommended.MemoryMiB > 0);
            Assert.True(e.Recommended.CpuCores > 0);
            Assert.True(e.Recommended.DiskGiB > 0);
            Assert.False(string.IsNullOrWhiteSpace(e.Recommended.Firmware));
            Assert.False(string.IsNullOrWhiteSpace(e.SourceName));
            Assert.False(string.IsNullOrWhiteSpace(e.OsFamily));

            // Unattended install is gated to the families with a registered installer (ADR-0013/0016):
            // Ubuntu (autoinstall) and Debian (preseed). The flag must not leak to unsupported families.
            if (e.SupportsAutoinstall)
            {
                Assert.True(e.OsFamily is "ubuntu" or "debian", $"Unexpected autoinstall family: {e.OsFamily}");
            }
        }

        // Ids must be unique — they key the selection and the cache-file fallback name.
        Assert.Equal(entries.Count, entries.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count());

        // The catalog must actually offer autoinstall for Ubuntu (the headline of the feature).
        Assert.Contains(entries, e => e.OsFamily == "ubuntu" && e.SupportsAutoinstall);
    }

    [Fact]
    public void Deserialize_ParsesEntries()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "entries": [
            {
              "id": "test-os", "name": "Test OS", "version": "1.0", "arch": "x86_64",
              "isoUrl": "https://example.com/test.iso",
              "sha256": "0000000000000000000000000000000000000000000000000000000000000000",
              "sizeBytes": 1234, "sourceName": "Example",
              "requiresLicense": true, "notes": "eval",
              "osFamily": "ubuntu", "supportsAutoinstall": true,
              "recommended": { "memoryMiB": 1024, "cpuCores": 1, "diskGiB": 10, "firmware": "bios" }
            }
          ]
        }
        """;

        OsCatalogDocument doc = OsCatalogJson.Deserialize(json);

        OsCatalogEntry e = Assert.Single(doc.Entries);
        Assert.Equal("test-os", e.Id);
        Assert.Equal(new Uri("https://example.com/test.iso"), e.IsoUrl);
        Assert.True(e.RequiresLicense);
        Assert.Equal("eval", e.Notes);
        Assert.Equal("ubuntu", e.OsFamily);
        Assert.True(e.SupportsAutoinstall);
        Assert.Equal(1024, e.Recommended.MemoryMiB);
        Assert.Equal("bios", e.Recommended.Firmware);
    }

    [Fact]
    public void Deserialize_AllowsCommentsAndTrailingCommas()
    {
        const string json = """
        {
          // a comment
          "schemaVersion": 1,
          "entries": [],
        }
        """;

        OsCatalogDocument doc = OsCatalogJson.Deserialize(json);

        Assert.Empty(doc.Entries);
    }

    [Fact]
    public void Deserialize_RejectsUnsupportedSchemaVersion()
    {
        const string json = """{ "schemaVersion": 999, "entries": [] }""";

        Assert.Throws<OsCatalogException>(() => OsCatalogJson.Deserialize(json));
    }

    [Fact]
    public void Deserialize_RejectsMalformedJson()
    {
        Assert.Throws<OsCatalogException>(() => OsCatalogJson.Deserialize("{ not valid json"));
    }
}

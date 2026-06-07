using Boxwright.Core;
using Xunit;

namespace Boxwright.Core.Tests;

public sealed class LiveSnapshotManifestJsonTests
{
    [Fact]
    public void SerializeThenDeserialize_RoundTrips()
    {
        var manifest = new LiveSnapshotManifest
        {
            Snapshots =
            [
                new LiveSnapshotEntry
                {
                    Id = "a1b2c3d4",
                    Name = "before-update",
                    CreatedUtc = DateTimeOffset.UnixEpoch,
                    Disks = [new LiveSnapshotDisk { DiskIndex = 0, FrozenFile = "disk.qcow2" }],
                },
            ],
        };

        LiveSnapshotManifest round = LiveSnapshotManifestJson.Deserialize(LiveSnapshotManifestJson.Serialize(manifest));

        LiveSnapshotEntry entry = Assert.Single(round.Snapshots);
        Assert.Equal("a1b2c3d4", entry.Id);
        Assert.Equal("before-update", entry.Name);
        Assert.Equal(DateTimeOffset.UnixEpoch, entry.CreatedUtc);
        Assert.Equal("disk.qcow2", Assert.Single(entry.Disks).FrozenFile);
    }

    [Fact]
    public async Task LoadAsync_MissingFile_ReturnsEmptyManifest()
    {
        string path = Path.Combine(Path.GetTempPath(), "bw-missing-" + Guid.NewGuid().ToString("N") + ".json");

        LiveSnapshotManifest manifest = await LiveSnapshotManifestJson.LoadAsync(path);

        Assert.Empty(manifest.Snapshots);
        Assert.Equal(LiveSnapshotManifest.CurrentSchemaVersion, manifest.SchemaVersion);
    }

    [Fact]
    public void Deserialize_UnsupportedSchemaVersion_Throws()
    {
        DiskException ex = Assert.Throws<DiskException>(() =>
            LiveSnapshotManifestJson.Deserialize("{\"schemaVersion\":999,\"snapshots\":[]}"));
        Assert.Contains("schemaVersion", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_MalformedJson_Throws() =>
        Assert.Throws<DiskException>(() => LiveSnapshotManifestJson.Deserialize("}{ not json"));
}

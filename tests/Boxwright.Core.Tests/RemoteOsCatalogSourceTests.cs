using Boxwright.Core;
using Xunit;

namespace Boxwright.Core.Tests;

public sealed class RemoteOsCatalogSourceTests
{
    private static readonly Uri Url = new("https://example.com/OsCatalog.json");
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(5);

    private static string CatalogJson(params string[] ids) =>
        "{\"schemaVersion\":1,\"entries\":[" +
        string.Join(",", ids.Select(id => $"{{\"id\":\"{id}\",\"name\":\"{id}\"}}")) +
        "]}";

    private static RemoteOsCatalogSource Create(
        IHttpStreamSource http, IOsCatalogSource bundled, string cacheFilePath) =>
        new(http, bundled, Url, cacheFilePath, ShortTimeout);

    [Fact]
    public async Task GetEntriesAsync_RemoteSucceeds_ReturnsRemoteAndWritesCache()
    {
        using var temp = new TempDir();
        string cache = Path.Combine(temp.Path, "os-catalog-cache.json");
        var http = new FakeHttpStreamSource(CatalogJson("remote-os"));
        var bundled = new FakeBundledSource("bundled-os");

        var source = Create(http, bundled, cache);
        IReadOnlyList<OsCatalogEntry> entries = await source.GetEntriesAsync();

        Assert.Equal("remote-os", Assert.Single(entries).Id);
        Assert.Equal(0, bundled.CallCount);
        Assert.True(File.Exists(cache));
        Assert.Contains("remote-os", await File.ReadAllTextAsync(cache));
    }

    [Fact]
    public async Task GetEntriesAsync_RemoteFails_FallsBackToCache()
    {
        using var temp = new TempDir();
        string cache = Path.Combine(temp.Path, "os-catalog-cache.json");
        await File.WriteAllTextAsync(cache, CatalogJson("cached-os"));
        var http = FakeHttpStreamSource.Throwing(new DownloadException("offline"));
        var bundled = new FakeBundledSource("bundled-os");

        var source = Create(http, bundled, cache);
        IReadOnlyList<OsCatalogEntry> entries = await source.GetEntriesAsync();

        Assert.Equal("cached-os", Assert.Single(entries).Id);
        Assert.Equal(0, bundled.CallCount); // cache served it; bundled untouched
    }

    [Fact]
    public async Task GetEntriesAsync_RemoteFailsAndNoCache_FallsBackToBundled()
    {
        using var temp = new TempDir();
        string cache = Path.Combine(temp.Path, "os-catalog-cache.json"); // does not exist
        var http = FakeHttpStreamSource.Throwing(new DownloadException("offline"));
        var bundled = new FakeBundledSource("bundled-os");

        var source = Create(http, bundled, cache);
        IReadOnlyList<OsCatalogEntry> entries = await source.GetEntriesAsync();

        Assert.Equal("bundled-os", Assert.Single(entries).Id);
        Assert.Equal(1, bundled.CallCount);
    }

    [Fact]
    public async Task GetEntriesAsync_RemoteReturnsMalformedJson_FallsBackAndDoesNotThrow()
    {
        using var temp = new TempDir();
        string cache = Path.Combine(temp.Path, "os-catalog-cache.json"); // no cache → bundled
        var http = new FakeHttpStreamSource("}{ this is not json");
        var bundled = new FakeBundledSource("bundled-os");

        var source = Create(http, bundled, cache);
        IReadOnlyList<OsCatalogEntry> entries = await source.GetEntriesAsync();

        Assert.Equal("bundled-os", Assert.Single(entries).Id);
        Assert.False(File.Exists(cache)); // a bad remote response must not overwrite the cache
    }

    [Fact]
    public async Task GetEntriesAsync_InternalTimeout_FallsBackToBundled()
    {
        using var temp = new TempDir();
        string cache = Path.Combine(temp.Path, "os-catalog-cache.json");
        var http = new FakeHttpStreamSource(CatalogJson("remote-os")); // would succeed, but never resolves in time
        var bundled = new FakeBundledSource("bundled-os");

        // A 1ms timeout fires before the (blocked) fake responds.
        var source = new RemoteOsCatalogSource(
            http.BlockingUntilCancelled(), bundled, Url, cache, TimeSpan.FromMilliseconds(1));
        IReadOnlyList<OsCatalogEntry> entries = await source.GetEntriesAsync();

        Assert.Equal("bundled-os", Assert.Single(entries).Id);
    }

    [Fact]
    public async Task GetEntriesAsync_CallerCancels_Propagates()
    {
        using var temp = new TempDir();
        string cache = Path.Combine(temp.Path, "os-catalog-cache.json");
        var http = new FakeHttpStreamSource(CatalogJson("remote-os")).BlockingUntilCancelled();
        var bundled = new FakeBundledSource("bundled-os");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var source = Create(http, bundled, cache);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.GetEntriesAsync(cts.Token));
        Assert.Equal(0, bundled.CallCount); // caller-cancellation is not a fallback
    }

    [Fact]
    public async Task GetEntriesAsync_SecondCall_IsMemoizedAndDoesNotRefetch()
    {
        using var temp = new TempDir();
        string cache = Path.Combine(temp.Path, "os-catalog-cache.json");
        var http = new FakeHttpStreamSource(CatalogJson("remote-os"));
        var bundled = new FakeBundledSource("bundled-os");

        var source = Create(http, bundled, cache);
        _ = await source.GetEntriesAsync();
        IReadOnlyList<OsCatalogEntry> second = await source.GetEntriesAsync();

        Assert.Equal("remote-os", Assert.Single(second).Id);
        Assert.Equal(1, http.OpenCount); // network touched exactly once
    }

    // ---- test doubles ----

    private sealed class FakeHttpStreamSource : IHttpStreamSource
    {
        private readonly string? _body;
        private readonly Exception? _throw;
        private bool _blockUntilCancelled;

        public FakeHttpStreamSource(string body) => _body = body;

        private FakeHttpStreamSource(Exception toThrow) => _throw = toThrow;

        public static FakeHttpStreamSource Throwing(Exception ex) => new(ex);

        /// <summary>Makes the source hang until its token is cancelled (drives timeout/cancel paths).</summary>
        public FakeHttpStreamSource BlockingUntilCancelled()
        {
            _blockUntilCancelled = true;
            return this;
        }

        public int OpenCount { get; private set; }

        public async Task<HttpDownload> OpenReadAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            OpenCount++;
            if (_blockUntilCancelled)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            if (_throw is not null)
            {
                throw _throw;
            }

            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(_body!);
            return new HttpDownload(new MemoryStream(bytes, writable: false), bytes.Length);
        }
    }

    private sealed class FakeBundledSource(string entryId) : IOsCatalogSource
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<OsCatalogEntry>> GetEntriesAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            IReadOnlyList<OsCatalogEntry> entries = [new OsCatalogEntry { Id = entryId, Name = entryId }];
            return Task.FromResult(entries);
        }
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bw-cat-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}

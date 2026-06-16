using Boxwright.Core;
using Microsoft.Extensions.Logging;
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

    private static RemoteOsCatalogSource Create(
        IHttpStreamSource http,
        IOsCatalogSource bundled,
        string cacheFilePath,
        TimeSpan stalenessWindow,
        TimeProvider timeProvider,
        ILogger<RemoteOsCatalogSource>? logger = null) =>
        new(http, bundled, Url, cacheFilePath, ShortTimeout, logger, stalenessWindow, timeProvider);

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

    // ---- freshness (ADR-0020) ----

    [Fact]
    public void GetFreshness_BeforeAnyLoad_IsUnknown()
    {
        using var temp = new TempDir();
        var http = new FakeHttpStreamSource(CatalogJson("remote-os"));
        var source = Create(http, new FakeBundledSource("bundled-os"), Path.Combine(temp.Path, "c.json"));

        // No GetEntriesAsync call yet: must report Unknown rather than throw.
        Assert.Equal(OsCatalogFreshnessState.Unknown, source.GetFreshness().State);
    }

    [Fact]
    public async Task RemoteSuccess_StampsCacheWithProviderTime_AndReportsRemote()
    {
        using var temp = new TempDir();
        string cache = Path.Combine(temp.Path, "c.json");
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        var source = Create(new FakeHttpStreamSource(CatalogJson("remote-os")), new FakeBundledSource("b"), cache, TimeSpan.FromDays(21), clock);

        _ = await source.GetEntriesAsync();

        Assert.Equal(OsCatalogFreshnessState.Remote, source.GetFreshness().State);
        Assert.False(source.GetFreshness().IsStale);
        // WriteCache must stamp the cache mtime from the injected clock, not wall-clock.
        Assert.Equal(now.UtcDateTime, File.GetLastWriteTimeUtc(cache));
    }

    [Fact]
    public async Task RemoteOffline_FreshCache_ServedSilently()
    {
        using var temp = new TempDir();
        string cache = Path.Combine(temp.Path, "c.json");
        await File.WriteAllTextAsync(cache, CatalogJson("cached-os"));
        var cachedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        File.SetLastWriteTimeUtc(cache, cachedAt.UtcDateTime);
        // "Now" is 5 days after the cache was written — well within a 21-day window.
        var clock = new FakeTimeProvider(cachedAt.AddDays(5));
        var logger = new CapturingLogger();
        var source = Create(FakeHttpStreamSource.Throwing(new DownloadException("offline")), new FakeBundledSource("b"), cache, TimeSpan.FromDays(21), clock, logger);

        IReadOnlyList<OsCatalogEntry> entries = await source.GetEntriesAsync();

        Assert.Equal("cached-os", Assert.Single(entries).Id);
        OsCatalogFreshness f = source.GetFreshness();
        Assert.Equal(OsCatalogFreshnessState.FreshCache, f.State);
        Assert.False(f.IsStale);
        Assert.Equal(0, logger.WarningCount); // fresh cache is silent
    }

    [Fact]
    public async Task RemoteOffline_StaleCache_FlaggedAndWarned()
    {
        using var temp = new TempDir();
        string cache = Path.Combine(temp.Path, "c.json");
        await File.WriteAllTextAsync(cache, CatalogJson("cached-os"));
        var cachedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        File.SetLastWriteTimeUtc(cache, cachedAt.UtcDateTime);
        // 30 days later, past the 21-day window.
        var clock = new FakeTimeProvider(cachedAt.AddDays(30));
        var logger = new CapturingLogger();
        var source = Create(FakeHttpStreamSource.Throwing(new DownloadException("offline")), new FakeBundledSource("b"), cache, TimeSpan.FromDays(21), clock, logger);

        IReadOnlyList<OsCatalogEntry> entries = await source.GetEntriesAsync();

        Assert.Equal("cached-os", Assert.Single(entries).Id); // behaviour preserved: stale cache still served
        OsCatalogFreshness f = source.GetFreshness();
        Assert.Equal(OsCatalogFreshnessState.StaleCache, f.State);
        Assert.True(f.IsStale);
        Assert.Equal(TimeSpan.FromDays(30), f.Age);
        Assert.Equal(1, logger.WarningCount); // surfaced exactly once
    }

    [Fact]
    public async Task RemoteOffline_NoCache_BundledIsNotStale()
    {
        using var temp = new TempDir();
        string cache = Path.Combine(temp.Path, "c.json"); // does not exist
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var logger = new CapturingLogger();
        var source = Create(FakeHttpStreamSource.Throwing(new DownloadException("offline")), new FakeBundledSource("bundled-os"), cache, TimeSpan.FromDays(21), clock, logger);

        IReadOnlyList<OsCatalogEntry> entries = await source.GetEntriesAsync();

        Assert.Equal("bundled-os", Assert.Single(entries).Id);
        OsCatalogFreshness f = source.GetFreshness();
        Assert.Equal(OsCatalogFreshnessState.Bundled, f.State);
        Assert.False(f.IsStale); // shipped baseline, never "stale"
        Assert.Equal(0, logger.WarningCount);
    }

    [Fact]
    public async Task StalenessBoundary_AgeEqualsWindow_IsFresh()
    {
        using var temp = new TempDir();
        string cache = Path.Combine(temp.Path, "c.json");
        await File.WriteAllTextAsync(cache, CatalogJson("cached-os"));
        var cachedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        File.SetLastWriteTimeUtc(cache, cachedAt.UtcDateTime);
        // Exactly at the window: strict '>' means this is NOT stale.
        var window = TimeSpan.FromDays(21);
        var clock = new FakeTimeProvider(cachedAt.Add(window));
        var source = Create(FakeHttpStreamSource.Throwing(new DownloadException("offline")), new FakeBundledSource("b"), cache, window, clock);

        _ = await source.GetEntriesAsync();

        Assert.Equal(OsCatalogFreshnessState.FreshCache, source.GetFreshness().State);
    }

    [Fact]
    public async Task StalenessWindow_IsHonoured_SameAgeFreshUnderLongStaleUnderShort()
    {
        var cachedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(cachedAt.AddDays(20));

        using (var temp = new TempDir())
        {
            string cache = Path.Combine(temp.Path, "c.json");
            await File.WriteAllTextAsync(cache, CatalogJson("cached-os"));
            File.SetLastWriteTimeUtc(cache, cachedAt.UtcDateTime);
            var longWindow = Create(FakeHttpStreamSource.Throwing(new DownloadException("offline")), new FakeBundledSource("b"), cache, TimeSpan.FromDays(60), clock);
            _ = await longWindow.GetEntriesAsync();
            Assert.Equal(OsCatalogFreshnessState.FreshCache, longWindow.GetFreshness().State);
        }

        using (var temp = new TempDir())
        {
            string cache = Path.Combine(temp.Path, "c.json");
            await File.WriteAllTextAsync(cache, CatalogJson("cached-os"));
            File.SetLastWriteTimeUtc(cache, cachedAt.UtcDateTime);
            var shortWindow = Create(FakeHttpStreamSource.Throwing(new DownloadException("offline")), new FakeBundledSource("b"), cache, TimeSpan.FromDays(7), clock);
            _ = await shortWindow.GetEntriesAsync();
            Assert.Equal(OsCatalogFreshnessState.StaleCache, shortWindow.GetFreshness().State);
        }
    }

    [Fact]
    public async Task Composite_ForwardsWrappedRemoteFreshness()
    {
        using var temp = new TempDir();
        string cache = Path.Combine(temp.Path, "c.json");
        await File.WriteAllTextAsync(cache, CatalogJson("cached-os"));
        var cachedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        File.SetLastWriteTimeUtc(cache, cachedAt.UtcDateTime);
        var clock = new FakeTimeProvider(cachedAt.AddDays(40));
        var remote = Create(FakeHttpStreamSource.Throwing(new DownloadException("offline")), new FakeBundledSource("b"), cache, TimeSpan.FromDays(21), clock);
        var composite = new CompositeOsCatalogSource([remote, new FakeBundledSource("recipe-os")]);

        _ = await composite.GetEntriesAsync();

        Assert.Equal(OsCatalogFreshnessState.StaleCache, composite.GetFreshness().State);
    }

    [Fact]
    public void Composite_NoFreshnessAwareSource_ReportsUnknown()
    {
        var composite = new CompositeOsCatalogSource([new FakeBundledSource("a"), new FakeBundledSource("b")]);
        Assert.Equal(OsCatalogFreshnessState.Unknown, composite.GetFreshness().State);
    }

    // ---- test doubles ----

    /// <summary>A minimal settable clock so freshness is deterministic without a new package dependency.</summary>
    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>Counts warning-level log entries so tests can assert a stale cache is surfaced exactly once.</summary>
    private sealed class CapturingLogger : ILogger<RemoteOsCatalogSource>
    {
        public int WarningCount { get; private set; }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                WarningCount++;
            }
        }
    }

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

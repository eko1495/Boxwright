using System.Security.Cryptography;
using Boxwright.Core;
using Xunit;

namespace Boxwright.Core.Tests;

public sealed class IsoDownloaderTests
{
    private static OsCatalogEntry Entry(string sha256, long size = 0, string url = "https://example.com/test.iso") => new()
    {
        Id = "test-os",
        Name = "Test OS",
        Version = "1.0",
        IsoUrl = new Uri(url),
        Sha256 = sha256,
        SizeBytes = size,
        SourceName = "Example",
    };

    private static string Sha256Hex(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    [Fact]
    public async Task EnsureAsync_VerifiedDownload_WritesFileAndMarker()
    {
        using var temp = new TempDir();
        byte[] content = [1, 2, 3, 4, 5, 6, 7, 8];
        var http = new FakeHttpStreamSource(content);
        var downloader = new IsoDownloader(http, temp.Path);

        string path = await downloader.EnsureAsync(Entry(Sha256Hex(content)));

        Assert.True(File.Exists(path));
        Assert.Equal(content, await File.ReadAllBytesAsync(path));
        Assert.True(File.Exists(path + ".sha256"));
        Assert.Equal(Sha256Hex(content), (await File.ReadAllTextAsync(path + ".sha256")).Trim());
        Assert.Empty(Directory.GetFiles(temp.Path, "*.part"));
        Assert.Equal(1, http.OpenCount);
    }

    [Fact]
    public async Task EnsureAsync_ChecksumMismatch_ThrowsAndLeavesNothing()
    {
        using var temp = new TempDir();
        byte[] content = [9, 9, 9, 9];
        var http = new FakeHttpStreamSource(content);
        var downloader = new IsoDownloader(http, temp.Path);

        await Assert.ThrowsAsync<DownloadException>(() =>
            downloader.EnsureAsync(Entry("deadbeef" + new string('0', 56))));

        Assert.Empty(Directory.GetFiles(temp.Path)); // no .iso, no .part, no marker
    }

    [Fact]
    public async Task EnsureAsync_Cancelled_ThrowsAndCleansUpPartial()
    {
        using var temp = new TempDir();
        using var cts = new CancellationTokenSource();
        var http = new FakeHttpStreamSource(new CancelDuringReadStream(cts), declaredTotal: 1000);
        var downloader = new IsoDownloader(http, temp.Path);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            downloader.EnsureAsync(Entry(new string('a', 64)), progress: null, cts.Token));

        Assert.Empty(Directory.GetFiles(temp.Path)); // the .part was cleaned up
    }

    [Fact]
    public async Task EnsureAsync_CacheHit_DoesNotDownloadAgain()
    {
        using var temp = new TempDir();
        byte[] content = [4, 2];
        string hash = Sha256Hex(content);
        // Pre-seed a verified cached file + marker (content is arbitrary; cache-hit must not re-hash).
        string cached = Path.Combine(temp.Path, "test.iso");
        await File.WriteAllBytesAsync(cached, content);
        await File.WriteAllTextAsync(cached + ".sha256", hash);
        var http = new FakeHttpStreamSource([0xFF]); // would mismatch if it were ever read
        var downloader = new IsoDownloader(http, temp.Path);

        string path = await downloader.EnsureAsync(Entry(hash));

        Assert.Equal(cached, path);
        Assert.Equal(0, http.OpenCount); // network never touched
    }

    [Fact]
    public async Task EnsureAsync_StaleMarker_ReDownloads()
    {
        using var temp = new TempDir();
        byte[] content = [5, 5, 5, 5];
        string cached = Path.Combine(temp.Path, "test.iso");
        await File.WriteAllBytesAsync(cached, [0, 0]);
        await File.WriteAllTextAsync(cached + ".sha256", new string('b', 64)); // wrong hash
        var http = new FakeHttpStreamSource(content);
        var downloader = new IsoDownloader(http, temp.Path);

        string path = await downloader.EnsureAsync(Entry(Sha256Hex(content)));

        Assert.Equal(content, await File.ReadAllBytesAsync(path));
        Assert.Equal(1, http.OpenCount);
    }

    [Fact]
    public async Task EnsureAsync_ReportsProgressEndingAtTotal()
    {
        using var temp = new TempDir();
        byte[] content = new byte[1000];
        var http = new FakeHttpStreamSource(content, declaredTotal: content.Length);
        var downloader = new IsoDownloader(http, temp.Path);
        var progress = new RecordingProgress();

        await downloader.EnsureAsync(Entry(Sha256Hex(content)), progress);

        Assert.NotEmpty(progress.Reports);
        IsoDownloadProgress last = progress.Reports[^1];
        Assert.Equal(content.Length, last.BytesReceived);
        Assert.Equal(100d, last.Percent);
    }

    [Fact]
    public async Task EnsureAsync_DownloadFailure_SurfacesDownloadException()
    {
        // The IHttpStreamSource contract wraps network/status failures as DownloadException
        // (HttpClientStreamSource does this); the downloader surfaces it and leaves no file.
        using var temp = new TempDir();
        var http = FakeHttpStreamSource.Throwing(new DownloadException("network error"));
        var downloader = new IsoDownloader(http, temp.Path);

        await Assert.ThrowsAsync<DownloadException>(() => downloader.EnsureAsync(Entry(new string('a', 64))));
        Assert.Empty(Directory.GetFiles(temp.Path));
    }

    // ---- test doubles ----

    private sealed class FakeHttpStreamSource : IHttpStreamSource
    {
        private readonly Func<HttpDownload>? _factory;
        private readonly Exception? _throw;

        public FakeHttpStreamSource(byte[] content, long? declaredTotal = null) =>
            _factory = () => new HttpDownload(new MemoryStream(content, writable: false), declaredTotal ?? content.Length);

        public FakeHttpStreamSource(Stream stream, long? declaredTotal) =>
            _factory = () => new HttpDownload(stream, declaredTotal);

        private FakeHttpStreamSource(Exception toThrow) => _throw = toThrow;

        public static FakeHttpStreamSource Throwing(Exception ex) => new(ex);

        public int OpenCount { get; private set; }

        public Task<HttpDownload> OpenReadAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            OpenCount++;
            return _throw is not null
                ? Task.FromException<HttpDownload>(_throw)
                : Task.FromResult(_factory!());
        }
    }

    private sealed class RecordingProgress : IProgress<IsoDownloadProgress>
    {
        public List<IsoDownloadProgress> Reports { get; } = [];

        public void Report(IsoDownloadProgress value) => Reports.Add(value);
    }

    // Yields nothing; cancels the shared token on the first read so the downloader observes it.
    private sealed class CancelDuringReadStream(CancellationTokenSource cts) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cts.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(0);
        }
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bw-iso-" + Guid.NewGuid().ToString("N"));
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

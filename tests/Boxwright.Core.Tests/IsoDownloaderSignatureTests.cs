using System.Security.Cryptography;
using System.Text;
using Boxwright.Core;
using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;
using Xunit;

namespace Boxwright.Core.Tests;

// ADR-0027 phase 2: the OpenPGP provenance gate wired into IsoDownloader. Each test mints a throwaway PGP
// key (via the shared PgpTestKeys helper), signs a SHA256SUMS-style document in-process, and serves the
// ISO + checksums + signature over a fake IHttpStreamSource — no real keys, no real network. The gate is
// an ADDITIONAL check after SHA-256: a valid signature trusts the download, any failure throws
// DownloadException and leaves nothing.
public sealed class IsoDownloaderSignatureTests
{
    private const string IsoUrl = "https://example.com/path/test.iso";
    private const string ChecksumsUrl = "https://example.com/path/SHA256SUMS";
    private const string SignatureUrl = "https://example.com/path/SHA256SUMS.gpg";
    private const string KeyId = "test-distro";

    private static readonly byte[] IsoContent = [1, 2, 3, 4, 5, 6, 7, 8];

    private static string Sha256Hex(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    private static OsCatalogEntry SignedEntry(string? checksumsFileName = null) => new()
    {
        Id = "test-os",
        Name = "Test OS",
        Version = "1.0",
        IsoUrl = new Uri(IsoUrl),
        Sha256 = Sha256Hex(IsoContent),
        SourceName = "Example",
        Signature = new OsCatalogSignature
        {
            ChecksumsUrl = new Uri(ChecksumsUrl),
            SignatureUrl = new Uri(SignatureUrl),
            KeyId = KeyId,
            ChecksumsFileName = checksumsFileName,
        },
    };

    // A SHA256SUMS document listing this entry's ISO (and a decoy line, to prove filename pinning works).
    private static byte[] ChecksumsDocument(string hash, string fileName) =>
        Encoding.UTF8.GetBytes(
            $"{new string('f', 64)}  some-other-image.iso\n{hash} *{fileName}\n");

    [Fact]
    public async Task ValidSignature_TrustsDownload_AndWritesFileAndMarker()
    {
        using var temp = new TempDir();
        PgpSecretKey key = PgpTestKeys.NewKey();
        byte[] checksums = ChecksumsDocument(Sha256Hex(IsoContent), "test.iso");
        var http = MultiHttp.Serving(checksums, PgpTestKeys.Sign(key, checksums));
        var downloader = Downloader(http, temp.Path, PgpTestKeys.NewProvider(key));

        string path = await downloader.EnsureAsync(SignedEntry());

        Assert.Equal(IsoContent, await File.ReadAllBytesAsync(path));
        Assert.True(File.Exists(path + ".sha256"));
    }

    [Fact]
    public async Task ValidSignature_CacheHit_ReTrustsViaMarker_WithZeroFetches()
    {
        // The marker is written only after the full gate passed, so a later request re-trusts without
        // re-fetching the ISO, checksums, or signature — a signed entry's cache hit makes no network calls.
        using var temp = new TempDir();
        string hash = Sha256Hex(IsoContent);
        string cached = Path.Combine(temp.Path, "test.iso");
        await File.WriteAllBytesAsync(cached, IsoContent);
        await File.WriteAllTextAsync(cached + ".sha256", hash);
        var http = MultiHttp.Serving([], []); // any fetch would be observed
        var downloader = Downloader(http, temp.Path, PgpTestKeys.NewProvider(PgpTestKeys.NewKey()));

        string path = await downloader.EnsureAsync(SignedEntry());

        Assert.Equal(cached, path);
        Assert.Equal(0, http.TotalFetches);
    }

    [Fact]
    public async Task TamperedChecksums_ThrowsAndLeavesNothing()
    {
        // The signature was made over the genuine document; serving a tampered one makes Verify return
        // false (well-formed but doesn't match), which the gate turns into a discarded download.
        using var temp = new TempDir();
        PgpSecretKey key = PgpTestKeys.NewKey();
        byte[] genuine = ChecksumsDocument(Sha256Hex(IsoContent), "test.iso");
        byte[] signature = PgpTestKeys.Sign(key, genuine);
        byte[] tampered = ChecksumsDocument(Sha256Hex(IsoContent), "test.iso");
        tampered[0] ^= 0xFF;
        var http = MultiHttp.Serving(tampered, signature);
        var downloader = Downloader(http, temp.Path, PgpTestKeys.NewProvider(key));

        await Assert.ThrowsAsync<DownloadException>(() => downloader.EnsureAsync(SignedEntry()));
        Assert.Empty(Directory.GetFiles(temp.Path));
    }

    [Fact]
    public async Task WrongKey_ThrowsAndLeavesNothing()
    {
        // The bundled key is a different key than signed the checksums: no key id match -> OpenPgpException
        // -> DownloadException.
        using var temp = new TempDir();
        byte[] checksums = ChecksumsDocument(Sha256Hex(IsoContent), "test.iso");
        byte[] signature = PgpTestKeys.Sign(PgpTestKeys.NewKey(), checksums);
        var http = MultiHttp.Serving(checksums, signature);
        var downloader = Downloader(http, temp.Path, PgpTestKeys.NewProvider(PgpTestKeys.NewKey()));

        await Assert.ThrowsAsync<DownloadException>(() => downloader.EnsureAsync(SignedEntry()));
        Assert.Empty(Directory.GetFiles(temp.Path));
    }

    [Fact]
    public async Task MissingSignatureUrl_ThrowsAndLeavesNothing()
    {
        using var temp = new TempDir();
        PgpSecretKey key = PgpTestKeys.NewKey();
        byte[] checksums = ChecksumsDocument(Sha256Hex(IsoContent), "test.iso");
        // The signature URL fails (e.g. 404, surfaced as DownloadException by the IHttpStreamSource).
        var http = MultiHttp.Serving(checksums, signature: null);
        var downloader = Downloader(http, temp.Path, PgpTestKeys.NewProvider(key));

        await Assert.ThrowsAsync<DownloadException>(() => downloader.EnsureAsync(SignedEntry()));
        Assert.Empty(Directory.GetFiles(temp.Path));
    }

    [Fact]
    public async Task UnknownBundledKeyId_ThrowsAndLeavesNothing()
    {
        using var temp = new TempDir();
        PgpSecretKey key = PgpTestKeys.NewKey();
        byte[] checksums = ChecksumsDocument(Sha256Hex(IsoContent), "test.iso");
        var http = MultiHttp.Serving(checksums, PgpTestKeys.Sign(key, checksums));
        // The provider knows a different id than the entry references, so OpenPublicKey returns null.
        var downloader = Downloader(http, temp.Path, PgpTestKeys.NewProvider(key, registeredId: "other-distro"));

        await Assert.ThrowsAsync<DownloadException>(() => downloader.EnsureAsync(SignedEntry()));
        Assert.Empty(Directory.GetFiles(temp.Path));
    }

    [Fact]
    public async Task HashPresentButFilenameMismatch_ThrowsAndLeavesNothing()
    {
        // The correct hash is signed, but listed against a DIFFERENT filename. Pinning the hash to the
        // expected file rejects it — a multi-image SHA256SUMS can't have one image's hash matched to
        // another's name.
        using var temp = new TempDir();
        PgpSecretKey key = PgpTestKeys.NewKey();
        byte[] checksums = ChecksumsDocument(Sha256Hex(IsoContent), "not-our-image.iso");
        var http = MultiHttp.Serving(checksums, PgpTestKeys.Sign(key, checksums));
        var downloader = Downloader(http, temp.Path, PgpTestKeys.NewProvider(key));

        await Assert.ThrowsAsync<DownloadException>(() => downloader.EnsureAsync(SignedEntry()));
        Assert.Empty(Directory.GetFiles(temp.Path));
    }

    [Fact]
    public async Task ExplicitChecksumsFileName_IsUsedForMatching()
    {
        // When the catalog overrides the filename (the ISO is published under a different name than its
        // URL segment), matching uses the override, not the URL's last segment.
        using var temp = new TempDir();
        PgpSecretKey key = PgpTestKeys.NewKey();
        byte[] checksums = ChecksumsDocument(Sha256Hex(IsoContent), "published-name.iso");
        var http = MultiHttp.Serving(checksums, PgpTestKeys.Sign(key, checksums));
        var downloader = Downloader(http, temp.Path, PgpTestKeys.NewProvider(key));

        string path = await downloader.EnsureAsync(SignedEntry(checksumsFileName: "published-name.iso"));

        Assert.Equal(IsoContent, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task EntryWithoutSignature_IsUnchangedSha256Only_AndNeverFetchesSignatureUrls()
    {
        // An entry that doesn't opt in behaves exactly as before: SHA-256 verifies, and the checksums /
        // signature URLs are never touched (the gate is opt-in per entry).
        using var temp = new TempDir();
        var http = MultiHttp.Serving([], []);
        var downloader = Downloader(http, temp.Path, PgpTestKeys.NewProvider(PgpTestKeys.NewKey()));
        OsCatalogEntry plain = new()
        {
            Id = "plain",
            Name = "Plain OS",
            Version = "1.0",
            IsoUrl = new Uri(IsoUrl),
            Sha256 = Sha256Hex(IsoContent),
            SourceName = "Example",
        };

        string path = await downloader.EnsureAsync(plain);

        Assert.Equal(IsoContent, await File.ReadAllBytesAsync(path));
        Assert.Equal(0, http.AuxFetches); // only the ISO was fetched
    }

    [Fact]
    public async Task SignedEntry_WithoutVerifierConfigured_ThrowsRatherThanDowngrade()
    {
        // A downloader built without a verifier must refuse a signed entry, not silently fall back to
        // SHA-256-only — that would defeat the opt-in provenance gate.
        using var temp = new TempDir();
        byte[] checksums = ChecksumsDocument(Sha256Hex(IsoContent), "test.iso");
        var http = MultiHttp.Serving(checksums, PgpTestKeys.Sign(PgpTestKeys.NewKey(), checksums));
        var downloader = new IsoDownloader(http, temp.Path); // legacy ctor: no PGP, no keys

        await Assert.ThrowsAsync<DownloadException>(() => downloader.EnsureAsync(SignedEntry()));
        Assert.Empty(Directory.GetFiles(temp.Path));
    }

    private static IsoDownloader Downloader(IHttpStreamSource http, string cacheDir, ITrustedKeyProvider keys) =>
        new(http, cacheDir, new OpenPgpVerifier(), keys);

    // ---- test doubles ----

    // Serves the ISO, the checksums document, and the signature each from its own URL, counting fetches so
    // tests can assert which (if any) were touched. A null body for an aux URL simulates an unfetchable
    // resource (404), surfaced as DownloadException per the IHttpStreamSource contract.
    private sealed class MultiHttp : IHttpStreamSource
    {
        private readonly byte[]? _checksums;
        private readonly byte[]? _signature;

        private MultiHttp(byte[]? checksums, byte[]? signature)
        {
            _checksums = checksums;
            _signature = signature;
        }

        public static MultiHttp Serving(byte[]? checksums, byte[]? signature) => new(checksums, signature);

        public int AuxFetches { get; private set; }

        public int TotalFetches { get; private set; }

        public Task<HttpDownload> OpenReadAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            TotalFetches++;
            byte[]? body = uri.ToString() switch
            {
                IsoUrl => IsoContent,
                ChecksumsUrl => Bump(_checksums),
                SignatureUrl => Bump(_signature),
                _ => null,
            };

            return body is null
                ? Task.FromException<HttpDownload>(new DownloadException($"could not fetch {uri}"))
                : Task.FromResult(new HttpDownload(new MemoryStream(body, writable: false), body.Length));
        }

        private byte[]? Bump(byte[]? body)
        {
            AuxFetches++;
            return body;
        }
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bw-isosig-" + Guid.NewGuid().ToString("N"));
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

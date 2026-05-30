using Boxwright.App.Services;
using Xunit;

namespace Boxwright.App.Tests;

/// <summary>
/// Exercises <see cref="FileLogReader"/> over real temp files — including reading while a
/// writer holds the file open (as QEMU does), and tail-truncation of a large log.
/// </summary>
public sealed class FileLogReaderTests : IDisposable
{
    private readonly string _dir;
    private readonly FileLogReader _reader = new();

    public FileLogReaderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "boxwright-log-tests", Guid.NewGuid().ToString("N"));
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

    [Fact]
    public async Task ReadAsync_WhenFileMissing_ReturnsNull()
    {
        Assert.Null(await _reader.ReadAsync(Path.Combine(_dir, "nope.log")));
    }

    [Fact]
    public async Task ReadAsync_ReturnsFullContent_ForSmallFile()
    {
        string path = Path.Combine(_dir, "qemu.log");
        await File.WriteAllTextAsync(path, "hello\nworld");

        Assert.Equal("hello\nworld", await _reader.ReadAsync(path));
    }

    [Fact]
    public async Task ReadAsync_SucceedsWhileAnotherWriterHoldsTheFile()
    {
        string path = Path.Combine(_dir, "qemu.log");
        // Mirror QemuProcess: a live writer keeping the file open (FileShare.Read).
        using var writer = new StreamWriter(path, append: false) { AutoFlush = true };
        writer.WriteLine("line while open");

        string? content = await _reader.ReadAsync(path);

        Assert.NotNull(content);
        Assert.Contains("line while open", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_TruncatesLargeFile_ToTheTail()
    {
        string path = Path.Combine(_dir, "qemu.log");
        await File.WriteAllTextAsync(path, "HEAD-MARKER\n" + new string('x', 300 * 1024) + "\nTAIL-MARKER");

        string? content = await _reader.ReadAsync(path);

        Assert.NotNull(content);
        Assert.Contains("truncated", content, StringComparison.Ordinal);
        Assert.Contains("TAIL-MARKER", content, StringComparison.Ordinal);
        Assert.DoesNotContain("HEAD-MARKER", content, StringComparison.Ordinal);
    }
}

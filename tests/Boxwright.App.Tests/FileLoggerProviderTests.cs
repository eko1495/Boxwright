using Boxwright.App.Services;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Boxwright.App.Tests;

/// <summary>Exercises the minimal file logging provider — formatted line output and size-cap rollover.</summary>
public sealed class FileLoggerProviderTests : IDisposable
{
    private readonly string _dir;

    public FileLoggerProviderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "boxwright-filelog-tests", Guid.NewGuid().ToString("N"));
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
    public void Log_WritesFormattedLineWithShortCategory()
    {
        string path = Path.Combine(_dir, "boxwright.log");
        using (var provider = new FileLoggerProvider(path))
        {
            ILogger logger = provider.CreateLogger("Boxwright.Core.VmLauncher");
            logger.LogInformation("Starting VM {Name}.", "Ubuntu");
        }

        string log = File.ReadAllText(path);
        Assert.Contains("[Information]", log, StringComparison.Ordinal);
        Assert.Contains("VmLauncher:", log, StringComparison.Ordinal); // last namespace segment only
        Assert.Contains("Starting VM Ubuntu.", log, StringComparison.Ordinal);
    }

    [Fact]
    public void Log_RollsToOldFile_PastTheSizeCap()
    {
        string path = Path.Combine(_dir, "boxwright.log");
        using (var provider = new FileLoggerProvider(path))
        {
            ILogger logger = provider.CreateLogger("Test");
            string chunk = new('x', 64 * 1024);
            for (int i = 0; i < 40; i++) // > 2 MiB total forces exactly one rollover
            {
                logger.LogInformation("{Chunk}", chunk);
            }
        }

        Assert.True(File.Exists(path + ".old"), "expected a rolled-over .old log");
        Assert.True(File.Exists(path), "expected a fresh current log after rollover");
    }
}

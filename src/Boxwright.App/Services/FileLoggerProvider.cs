using Microsoft.Extensions.Logging;

namespace Boxwright.App.Services;

/// <summary>
/// A minimal, thread-safe file logging provider: appends formatted lines to a single log
/// file (auto-flushed), rolling once to <c>&lt;name&gt;.old</c> past a size cap. No
/// third-party dependency. This is app-wide diagnostics — distinct from per-VM qemu.log.
/// </summary>
internal sealed class FileLoggerProvider : ILoggerProvider
{
    private const long MaxBytes = 2 * 1024 * 1024;

    private readonly string _path;
    private readonly object _gate = new();
    private StreamWriter? _writer;

    public FileLoggerProvider(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, this);

    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    private void Write(string category, LogLevel level, string message, Exception? exception)
    {
        // Last namespace segment keeps lines short, e.g. "VmLauncher" not the full type name.
        int dot = category.LastIndexOf('.');
        string shortCategory = dot >= 0 ? category[(dot + 1)..] : category;

        lock (_gate)
        {
            try
            {
                _writer ??= new StreamWriter(_path, append: true) { AutoFlush = true };
                _writer.WriteLine($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}Z [{level}] {shortCategory}: {message}");
                if (exception is not null)
                {
                    _writer.WriteLine(exception);
                }

                RollIfNeeded();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Logging must never crash the app; drop this line.
            }
        }
    }

    private void RollIfNeeded()
    {
        if (_writer is null || _writer.BaseStream.Length < MaxBytes)
        {
            return;
        }

        _writer.Dispose();
        _writer = null;

        string archived = _path + ".old";
        File.Delete(archived); // no-op if it doesn't exist
        File.Move(_path, archived);
        // The next Write reopens a fresh log file.
    }

    private sealed class FileLogger(string category, FileLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            if (IsEnabled(logLevel))
            {
                provider.Write(category, logLevel, formatter(state, exception), exception);
            }
        }
    }
}

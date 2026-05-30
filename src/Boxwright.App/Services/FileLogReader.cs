namespace Boxwright.App.Services;

/// <summary>
/// The production <see cref="ILogReader"/>: reads a log file even while QEMU is still
/// writing it (<see cref="FileShare.ReadWrite"/>), returning only the last ~256 KiB so a
/// large log can't stall the (non-virtualized) log TextBox.
/// </summary>
internal sealed class FileLogReader : ILogReader
{
    private const long MaxBytes = 256 * 1024;

    public async Task<string?> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            bool truncated = stream.Length > MaxBytes;
            if (truncated)
            {
                stream.Seek(-MaxBytes, SeekOrigin.End);
            }

            using var reader = new StreamReader(stream);
            string text = await reader.ReadToEndAsync(cancellationToken);
            return truncated
                ? $"… (truncated — showing the last {MaxBytes / 1024} KiB) …{Environment.NewLine}{text}"
                : text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"(couldn't read the log: {ex.Message})";
        }
    }
}

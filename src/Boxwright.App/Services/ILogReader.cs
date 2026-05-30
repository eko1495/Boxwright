namespace Boxwright.App.Services;

/// <summary>
/// Reads a log file's text for display — tolerating a file QEMU is still writing and
/// capping very large logs. Returns null when the file doesn't exist yet. Abstracted
/// (like <see cref="IFilePicker"/>) so view models stay testable.
/// </summary>
public interface ILogReader
{
    /// <summary>Reads the file at <paramref name="path"/>; returns null if it doesn't exist.</summary>
    Task<string?> ReadAsync(string path, CancellationToken cancellationToken = default);
}

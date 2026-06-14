namespace Boxwright.Cli;

/// <summary>
/// A user-facing error that should be reported as a clean message (no stack trace) and
/// mapped to a process exit code. Thrown by commands for expected failures — a VM that
/// doesn't exist, a missing argument, an action that isn't valid in the current state.
/// </summary>
internal sealed class CliException : Exception
{
    /// <summary>The process exit code to return for this error (defaults to 1).</summary>
    public int ExitCode { get; }

    public CliException(string message, int exitCode = 1)
        : base(message) => ExitCode = exitCode;
}

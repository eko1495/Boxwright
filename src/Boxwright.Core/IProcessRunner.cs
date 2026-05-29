namespace Boxwright.Core;

/// <summary>
/// Runs an external program to completion and captures its output. Abstracted so
/// services that shell out (e.g. <c>qemu-img</c>, accelerator probes) are
/// unit-testable without launching a real process. QEMU and its tools are only
/// ever invoked across the process boundary — never linked or P/Invoked (ADR-0005).
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    /// Runs <paramref name="fileName"/> with <paramref name="arguments"/> to
    /// completion, returning its exit code and captured stdout/stderr.
    /// </summary>
    Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default);
}

/// <summary>The outcome of a finished process.</summary>
/// <param name="ExitCode">The process exit code.</param>
/// <param name="StandardOutput">Captured standard output.</param>
/// <param name="StandardError">Captured standard error.</param>
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

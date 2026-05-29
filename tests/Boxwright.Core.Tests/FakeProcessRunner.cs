namespace Boxwright.Core.Tests;

// Shared fake for services that shell out via IProcessRunner (CORE-4/CORE-6 use it).
// Records invocations and returns a scripted result.
internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Func<string, IReadOnlyList<string>, ProcessResult> _handler;

    public FakeProcessRunner(Func<string, IReadOnlyList<string>, ProcessResult> handler) => _handler = handler;

    public FakeProcessRunner(int exitCode, string standardOutput = "", string standardError = "")
        : this((_, _) => new ProcessResult(exitCode, standardOutput, standardError))
    {
    }

    public List<(string FileName, IReadOnlyList<string> Arguments)> Invocations { get; } = [];

    public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Invocations.Add((fileName, arguments));
        return Task.FromResult(_handler(fileName, arguments));
    }
}

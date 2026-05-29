using Xunit;

namespace Boxwright.Core.Tests;

// CORE-2: IProcessRunner + the real ProcessRunner.
public class ProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_CapturesStdoutAndExitCode()
    {
        var runner = new ProcessRunner();

        // `dotnet --version` is available wherever these tests run.
        ProcessResult result = await runner.RunAsync("dotnet", ["--version"]);

        Assert.Equal(0, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.StandardOutput));
    }

    [Fact]
    public async Task RunAsync_PreCancelledToken_Throws()
    {
        var runner = new ProcessRunner();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runner.RunAsync("dotnet", ["--version"], cts.Token));
    }

    [Fact]
    public async Task FakeProcessRunner_ReturnsScriptedResult_AndRecordsInvocation()
    {
        var fake = new FakeProcessRunner(exitCode: 0, standardOutput: "ok");

        ProcessResult result = await fake.RunAsync("qemu-img", ["--version"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("ok", result.StandardOutput);
        (string FileName, IReadOnlyList<string> Arguments) invocation = Assert.Single(fake.Invocations);
        Assert.Equal("qemu-img", invocation.FileName);
        Assert.Equal("--version", Assert.Single(invocation.Arguments));
    }
}

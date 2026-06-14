using Boxwright.Cli;
using Xunit;

namespace Boxwright.Cli.Tests;

public sealed class CommandDispatcherTests
{
    private sealed class FakeCommand : ICliCommand
    {
        private readonly int _exitCode;

        public FakeCommand(string name, int exitCode = 0)
        {
            Name = name;
            _exitCode = exitCode;
        }

        public string Name { get; }

        public string Summary => $"{Name} summary";

        public string Usage => $"{Name} <thing>";

        public ParsedArgs? Received { get; private set; }

        public Task<int> RunAsync(ParsedArgs args, CancellationToken cancellationToken)
        {
            Received = args;
            return Task.FromResult(_exitCode);
        }
    }

    private static CommandDispatcher Build(CapturingOutput output, params ICliCommand[] commands) =>
        new(commands, output.Cli);

    [Fact]
    public async Task No_args_prints_help()
    {
        var output = new CapturingOutput();
        CommandDispatcher dispatcher = Build(output, new FakeCommand("list"));

        int code = await dispatcher.DispatchAsync([], CancellationToken.None);

        Assert.Equal(0, code);
        Assert.Contains("Usage: boxwright", output.Out, StringComparison.Ordinal);
        Assert.Contains("list", output.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Version_flag_prints_version()
    {
        var output = new CapturingOutput();
        CommandDispatcher dispatcher = Build(output);

        int code = await dispatcher.DispatchAsync(["--version"], CancellationToken.None);

        Assert.Equal(0, code);
        Assert.Contains("boxwright", output.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_command_reports_to_stderr_and_returns_one()
    {
        var output = new CapturingOutput();
        CommandDispatcher dispatcher = Build(output, new FakeCommand("list"));

        int code = await dispatcher.DispatchAsync(["bogus"], CancellationToken.None);

        Assert.Equal(1, code);
        Assert.Contains("Unknown command", output.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dispatches_to_the_named_command_with_remaining_args()
    {
        var command = new FakeCommand("start", exitCode: 7);
        var output = new CapturingOutput();
        CommandDispatcher dispatcher = Build(output, command);

        int code = await dispatcher.DispatchAsync(["start", "devbox", "--detach"], CancellationToken.None);

        Assert.Equal(7, code);
        Assert.NotNull(command.Received);
        Assert.Equal(["devbox"], command.Received!.Positionals);
        Assert.True(command.Received.HasFlag("detach"));
    }

    [Fact]
    public async Task Command_help_flag_prints_usage_without_running()
    {
        var command = new FakeCommand("start");
        var output = new CapturingOutput();
        CommandDispatcher dispatcher = Build(output, command);

        int code = await dispatcher.DispatchAsync(["start", "--help"], CancellationToken.None);

        Assert.Equal(0, code);
        Assert.Null(command.Received); // not executed
        Assert.Contains("start <thing>", output.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Command_name_matching_is_case_insensitive()
    {
        var command = new FakeCommand("list");
        var output = new CapturingOutput();
        CommandDispatcher dispatcher = Build(output, command);

        int code = await dispatcher.DispatchAsync(["LIST"], CancellationToken.None);

        Assert.Equal(0, code);
        Assert.NotNull(command.Received);
    }
}

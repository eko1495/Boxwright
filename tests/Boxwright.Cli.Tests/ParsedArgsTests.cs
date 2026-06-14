using Boxwright.Cli;
using Xunit;

namespace Boxwright.Cli.Tests;

public sealed class ParsedArgsTests
{
    [Fact]
    public void Parse_separates_positionals_flags_and_options()
    {
        ParsedArgs args = ParsedArgs.Parse(["start", "devbox", "--detach", "--timeout=30"]);

        Assert.Equal(["start", "devbox"], args.Positionals);
        Assert.True(args.HasFlag("detach"));
        Assert.Equal("30", args.Option("timeout"));
    }

    [Fact]
    public void PositionalOrNull_returns_null_past_the_end()
    {
        ParsedArgs args = ParsedArgs.Parse(["only"]);

        Assert.Equal("only", args.PositionalOrNull(0));
        Assert.Null(args.PositionalOrNull(1));
    }

    [Fact]
    public void Flags_and_options_are_case_insensitive()
    {
        ParsedArgs args = ParsedArgs.Parse(["--Detach", "--Timeout=5"]);

        Assert.True(args.HasFlag("detach"));
        Assert.Equal("5", args.Option("timeout"));
    }

    [Fact]
    public void Missing_flag_and_option_report_absent()
    {
        ParsedArgs args = ParsedArgs.Parse(["vm"]);

        Assert.False(args.HasFlag("detach"));
        Assert.Null(args.Option("timeout"));
    }

    [Fact]
    public void Option_with_empty_value_is_preserved()
    {
        ParsedArgs args = ParsedArgs.Parse(["--note="]);

        Assert.Equal(string.Empty, args.Option("note"));
        Assert.False(args.HasFlag("note"));
    }

    [Fact]
    public void IntOption_parses_or_falls_back()
    {
        ParsedArgs withValue = ParsedArgs.Parse(["--memory=4096"]);
        ParsedArgs without = ParsedArgs.Parse([]);

        Assert.Equal(4096, withValue.IntOption("memory", 2048));
        Assert.Equal(2048, without.IntOption("memory", 2048));
    }

    [Fact]
    public void IntOption_throws_CliException_on_non_integer()
    {
        ParsedArgs args = ParsedArgs.Parse(["--memory=lots"]);

        CliException ex = Assert.Throws<CliException>(() => args.IntOption("memory", 2048));
        Assert.Contains("memory", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Lone_double_dash_is_treated_as_a_positional()
    {
        ParsedArgs args = ParsedArgs.Parse(["--"]);

        Assert.Equal(["--"], args.Positionals);
    }
}

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

    [Fact]
    public void Valued_option_accepts_the_space_form()
    {
        ParsedArgs args = ParsedArgs.Parse(["create", "vm", "--os", "ubuntu-24.04", "--memory", "4096"]);

        Assert.Equal(["create", "vm"], args.Positionals);
        Assert.Equal("ubuntu-24.04", args.Option("os"));
        Assert.Equal(4096, args.IntOption("memory", 0));
    }

    [Fact]
    public void Boolean_flag_does_not_consume_the_following_positional()
    {
        // --detach is a known boolean; "extra" must stay a positional, not become its value.
        ParsedArgs args = ParsedArgs.Parse(["start", "vm", "--detach", "extra"]);

        Assert.Equal(["start", "vm", "extra"], args.Positionals);
        Assert.True(args.HasFlag("detach"));
        Assert.Null(args.Option("detach"));
    }

    [Fact]
    public void Valued_option_before_another_option_has_no_value()
    {
        // --password is valued, but the next token is another option, so it gets no value.
        ParsedArgs args = ParsedArgs.Parse(["--password", "--user=x"]);

        Assert.Null(args.Option("password"));
        Assert.Equal("x", args.Option("user"));
    }

    [Fact]
    public void Equals_form_still_works_alongside_space_form()
    {
        ParsedArgs args = ParsedArgs.Parse(["--os=ubuntu", "--cpus", "2"]);

        Assert.Equal("ubuntu", args.Option("os"));
        Assert.Equal("2", args.Option("cpus"));
    }
}

using Boxwright.Cli;
using Xunit;

namespace Boxwright.Cli.Tests;

public sealed class TextTableTests
{
    [Fact]
    public void Render_pads_columns_to_the_widest_cell()
    {
        var table = new TextTable("NAME", "STATUS");
        table.AddRow("a", "running");
        table.AddRow("longer-name", "off");

        string[] lines = table.Render().Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // "NAME" pads to width 11 ("longer-name") + a two-space gap before the next column.
        Assert.Equal("NAME         STATUS", lines[0]);
        Assert.Equal("a            running", lines[1]);
        Assert.Equal("longer-name  off", lines[2]);
    }

    [Fact]
    public void Render_does_not_pad_the_final_column()
    {
        var table = new TextTable("A", "B");
        table.AddRow("x", "y");

        foreach (string line in table.Render().Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            Assert.Equal(line.TrimEnd(), line);
        }
    }

    [Fact]
    public void AddRow_rejects_a_wrong_cell_count()
    {
        var table = new TextTable("A", "B");

        Assert.Throws<ArgumentException>(() => table.AddRow("only-one"));
    }

    [Fact]
    public void Render_with_no_rows_emits_just_the_header()
    {
        var table = new TextTable("ONLY");

        Assert.Equal("ONLY\n", table.Render());
    }
}

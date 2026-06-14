using System.Text;

namespace Boxwright.Cli;

/// <summary>
/// Renders a simple left-aligned, space-padded column table for command output. No borders —
/// scriptable and easy on the eye. Column widths fit the widest cell (header or value).
/// </summary>
internal sealed class TextTable
{
    private const string ColumnGap = "  ";
    private readonly string[] _headers;
    private readonly List<string[]> _rows = [];

    public TextTable(params string[] headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        _headers = headers;
    }

    /// <summary>Adds a row; its cell count must match the header count.</summary>
    public void AddRow(params string[] cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        if (cells.Length != _headers.Length)
        {
            throw new ArgumentException(
                $"Row has {cells.Length} cells but the table has {_headers.Length} columns.", nameof(cells));
        }

        _rows.Add(cells);
    }

    /// <summary>Renders the header row plus all data rows, each terminated by a newline.</summary>
    public string Render()
    {
        int[] widths = new int[_headers.Length];
        for (int c = 0; c < _headers.Length; c++)
        {
            widths[c] = _headers[c].Length;
        }

        foreach (string[] row in _rows)
        {
            for (int c = 0; c < row.Length; c++)
            {
                widths[c] = Math.Max(widths[c], row[c].Length);
            }
        }

        var sb = new StringBuilder();
        AppendRow(sb, _headers, widths);
        foreach (string[] row in _rows)
        {
            AppendRow(sb, row, widths);
        }

        return sb.ToString();
    }

    private static void AppendRow(StringBuilder sb, string[] cells, int[] widths)
    {
        for (int c = 0; c < cells.Length; c++)
        {
            // The final column isn't padded — avoids trailing whitespace.
            sb.Append(c == cells.Length - 1 ? cells[c] : cells[c].PadRight(widths[c]) + ColumnGap);
        }

        sb.Append('\n');
    }
}

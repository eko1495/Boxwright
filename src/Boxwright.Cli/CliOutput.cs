namespace Boxwright.Cli;

/// <summary>
/// The CLI's output sinks, injected so commands never touch <see cref="Console"/> directly —
/// which keeps every command unit-testable against an in-memory writer.
/// </summary>
internal sealed class CliOutput
{
    public CliOutput(TextWriter standardOut, TextWriter standardError)
    {
        ArgumentNullException.ThrowIfNull(standardOut);
        ArgumentNullException.ThrowIfNull(standardError);
        Out = standardOut;
        Error = standardError;
    }

    /// <summary>Normal command output (results, tables).</summary>
    public TextWriter Out { get; }

    /// <summary>Diagnostics and error messages.</summary>
    public TextWriter Error { get; }

    /// <summary>Writes a line to standard output.</summary>
    public void Line(string text = "") => Out.WriteLine(text);

    /// <summary>Writes a line to standard error.</summary>
    public void ErrorLine(string text) => Error.WriteLine(text);
}

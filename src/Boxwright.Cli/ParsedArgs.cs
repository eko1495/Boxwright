namespace Boxwright.Cli;

/// <summary>
/// A parsed argument vector: ordered positionals plus options. Two unambiguous option
/// shapes are recognized — a boolean <c>--flag</c> and a valued <c>--key=value</c> — so
/// parsing never needs a per-command schema to know whether the next token is a value.
/// </summary>
internal sealed class ParsedArgs
{
    private readonly List<string> _positionals;
    private readonly Dictionary<string, string> _options;
    private readonly HashSet<string> _flags;

    private ParsedArgs(List<string> positionals, Dictionary<string, string> options, HashSet<string> flags)
    {
        _positionals = positionals;
        _options = options;
        _flags = flags;
    }

    /// <summary>The positional arguments, in order.</summary>
    public IReadOnlyList<string> Positionals => _positionals;

    /// <summary>Parses a raw argument vector into positionals, <c>--flag</c>s, and <c>--key=value</c> options.</summary>
    public static ParsedArgs Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var positionals = new List<string>();
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string token in args)
        {
            if (token.StartsWith("--", StringComparison.Ordinal) && token.Length > 2)
            {
                string body = token[2..];
                int eq = body.IndexOf('=', StringComparison.Ordinal);
                if (eq >= 0)
                {
                    options[body[..eq]] = body[(eq + 1)..];
                }
                else
                {
                    flags.Add(body);
                }
            }
            else
            {
                positionals.Add(token);
            }
        }

        return new ParsedArgs(positionals, options, flags);
    }

    /// <summary>The positional at <paramref name="index"/>, or null if there are fewer.</summary>
    public string? PositionalOrNull(int index) =>
        index >= 0 && index < _positionals.Count ? _positionals[index] : null;

    /// <summary>True if the boolean flag <c>--<paramref name="name"/></c> was supplied.</summary>
    public bool HasFlag(string name) => _flags.Contains(name);

    /// <summary>The value of <c>--<paramref name="name"/>=…</c>, or null if not supplied.</summary>
    public string? Option(string name) => _options.TryGetValue(name, out string? value) ? value : null;

    /// <summary>
    /// The value of <c>--<paramref name="name"/>=…</c> parsed as an integer, or
    /// <paramref name="fallback"/> if absent. Throws a <see cref="CliException"/> on a non-integer value.
    /// </summary>
    public int IntOption(string name, int fallback)
    {
        string? raw = Option(name);
        if (raw is null)
        {
            return fallback;
        }

        if (!int.TryParse(raw, out int value))
        {
            throw new CliException($"--{name} expects an integer, got '{raw}'.");
        }

        return value;
    }
}

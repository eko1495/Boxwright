namespace Boxwright.Cli;

/// <summary>
/// A parsed argument vector: ordered positionals plus options. Three shapes are recognized — a
/// boolean <c>--flag</c>, and a valued option as either <c>--key=value</c> or <c>--key value</c>.
/// Because a bare <c>--key</c> could be either a boolean or a valued option awaiting its value, the
/// known boolean flag names are listed in <see cref="BooleanFlags"/>; any other <c>--key</c> followed
/// by a non-option token consumes that token as its value.
/// </summary>
internal sealed class ParsedArgs
{
    // Every boolean flag the commands understand. Listed so the parser never mistakes the token that
    // follows a boolean flag (e.g. a positional) for the flag's "value".
    private static readonly HashSet<string> BooleanFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "detach", "display", "force", "yes", "linked", "unattended", "json", "now", "help",
    };

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

    /// <summary>Parses a raw argument vector into positionals, boolean flags, and valued options (<c>--key=value</c> or <c>--key value</c>).</summary>
    public static ParsedArgs Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var positionals = new List<string>();
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < args.Count; i++)
        {
            string token = args[i];
            if (!token.StartsWith("--", StringComparison.Ordinal) || token.Length <= 2)
            {
                positionals.Add(token);
                continue;
            }

            string body = token[2..];
            int eq = body.IndexOf('=', StringComparison.Ordinal);
            if (eq >= 0)
            {
                options[body[..eq]] = body[(eq + 1)..];
            }
            else if (BooleanFlags.Contains(body))
            {
                flags.Add(body);
            }
            else if (i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                // A valued option in space form: --key value. Consume the following token.
                options[body] = args[++i];
            }
            else
            {
                // An unknown bare flag (or a valued option with no value) — record it as a flag.
                flags.Add(body);
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

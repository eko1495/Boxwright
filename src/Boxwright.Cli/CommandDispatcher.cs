using System.Reflection;

namespace Boxwright.Cli;

/// <summary>
/// Routes a raw argument vector to a command by its first token, handling the global
/// <c>--help</c>/<c>--version</c> affordances and per-command <c>--help</c>.
/// </summary>
internal sealed class CommandDispatcher
{
    private readonly IReadOnlyList<ICliCommand> _commands;
    private readonly Dictionary<string, ICliCommand> _byName;
    private readonly CliOutput _output;

    public CommandDispatcher(IEnumerable<ICliCommand> commands, CliOutput output)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(output);
        _commands = commands.OrderBy(c => c.Name, StringComparer.Ordinal).ToList();
        _byName = _commands.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        _output = output;
    }

    /// <summary>Dispatches <paramref name="args"/> and returns the process exit code.</summary>
    public async Task<int> DispatchAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Count == 0)
        {
            PrintHelp();
            return 0;
        }

        string first = args[0];
        switch (first)
        {
            case "--help" or "-h" or "help":
                PrintHelp();
                return 0;
            case "--version" or "-v":
                _output.Line($"boxwright {Version}");
                return 0;
        }

        if (!_byName.TryGetValue(first, out ICliCommand? command))
        {
            _output.ErrorLine($"Unknown command '{first}'. Run 'boxwright --help' for the command list.");
            return 1;
        }

        ParsedArgs parsed = ParsedArgs.Parse(args.Skip(1).ToList());
        if (parsed.HasFlag("help"))
        {
            _output.Line($"Usage: boxwright {command.Usage}");
            _output.Line(command.Summary);
            return 0;
        }

        return await command.RunAsync(parsed, cancellationToken);
    }

    private void PrintHelp()
    {
        _output.Line("boxwright — headless control for Boxwright VMs (QEMU).");
        _output.Line();
        _output.Line("Usage: boxwright <command> [arguments] [--options]");
        _output.Line();
        _output.Line("Commands:");

        var table = new TextTable("  COMMAND", "DESCRIPTION");
        foreach (ICliCommand command in _commands)
        {
            table.AddRow($"  {command.Name}", command.Summary);
        }

        _output.Out.Write(table.Render());
        _output.Line();
        _output.Line("Run 'boxwright <command> --help' for a command's usage.");
        _output.Line("VMs are addressed by id, exact name, or a unique id prefix.");
    }

    // The CLI ships with the rest of the app, so its assembly version is the product version.
    private static string Version =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "0.0.0";
}

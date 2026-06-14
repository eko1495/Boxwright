namespace Boxwright.Cli;

/// <summary>
/// One top-level CLI command (e.g. <c>list</c>, <c>start</c>). Commands are resolved from DI
/// and dispatched by <see cref="Name"/>; they receive the already-parsed arguments (with the
/// command token removed) and return a process exit code.
/// </summary>
internal interface ICliCommand
{
    /// <summary>The command word the user types (e.g. <c>start</c>).</summary>
    string Name { get; }

    /// <summary>A one-line summary for <c>--help</c>.</summary>
    string Summary { get; }

    /// <summary>A usage line shown by <c>--help</c> and on a usage error (e.g. <c>start &lt;id|name&gt; [--detach]</c>).</summary>
    string Usage { get; }

    /// <summary>Runs the command. Returns the process exit code (0 = success).</summary>
    Task<int> RunAsync(ParsedArgs args, CancellationToken cancellationToken);
}

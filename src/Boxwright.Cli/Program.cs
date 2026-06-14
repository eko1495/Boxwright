using Boxwright.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Boxwright.Cli;

/// <summary>
/// The headless entry point (ADR-0022). Builds the service provider, wires Ctrl+C to a
/// cancellation token, dispatches the command, and maps expected failures to clean messages
/// and exit codes — no stack traces for user-level errors.
/// </summary>
internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var output = new CliOutput(Console.Out, Console.Error);
        using ServiceProvider services = CliServices.Build(output);

        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, eventArgs) =>
        {
            // Translate the first Ctrl+C into cooperative cancellation (e.g. a graceful VM shutdown)
            // rather than an abrupt process kill. A second Ctrl+C uses the default (terminate).
            eventArgs.Cancel = true;
            cts.Cancel();
        };
        Console.CancelKeyPress += onCancel;

        try
        {
            CommandDispatcher dispatcher = services.GetRequiredService<CommandDispatcher>();
            return await dispatcher.DispatchAsync(args, cts.Token);
        }
        catch (CliException ex)
        {
            output.ErrorLine($"error: {ex.Message}");
            return ex.ExitCode;
        }
        catch (OperationCanceledException)
        {
            output.ErrorLine("Cancelled.");
            return 130; // 128 + SIGINT, the conventional shell code for Ctrl+C.
        }
        catch (Exception ex) when (IsExpectedCoreError(ex))
        {
            output.ErrorLine($"error: {ex.Message}");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }

    // Expected, user-actionable failures from Core (a bad config, a missing qemu-img, a failed
    // download) are reported as a clean message. Anything else is a real bug and propagates with
    // its stack trace.
    private static bool IsExpectedCoreError(Exception ex) =>
        ex is VmConfigException
            or DiskException
            or DisplayException
            or DownloadException
            or OsCatalogException
            or QemuNotFoundException
            or InstallMediaException
            or IOException
            or UnauthorizedAccessException;
}

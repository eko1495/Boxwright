namespace Boxwright.Core;

/// <summary>The lifecycle state of a <see cref="QemuProcess"/>.</summary>
public enum QemuProcessState
{
    /// <summary>Not started yet.</summary>
    NotStarted,

    /// <summary>Started and running.</summary>
    Running,

    /// <summary>Exited (see <see cref="QemuProcess.ExitCode"/>).</summary>
    Exited,
}

/// <summary>
/// Supervises a single VM's <c>qemu-system-&lt;arch&gt;</c> child process (ADR-0003):
/// spawns it, captures combined stdout/stderr to a per-VM log file, tracks state,
/// and surfaces exit. Killing the process stops the VM.
/// </summary>
public sealed class QemuProcess : IDisposable
{
    private readonly IProcessLauncher _launcher;
    private readonly string _executable;
    private readonly IReadOnlyList<string> _arguments;
    private readonly string _workingDirectory;
    private readonly Accelerator? _accelerator;
    private readonly object _logGate = new();

    private IRunningProcess? _process;
    private StreamWriter? _log;
    private bool _disposed;

    /// <summary>Creates a supervisor for one QEMU process.</summary>
    public QemuProcess(
        IProcessLauncher launcher,
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string logFilePath,
        Accelerator? accelerator = null)
    {
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(logFilePath);

        _launcher = launcher;
        _executable = executable;
        _arguments = arguments;
        _workingDirectory = workingDirectory;
        _accelerator = accelerator;
        LogFilePath = logFilePath;
    }

    /// <summary>The current lifecycle state.</summary>
    public QemuProcessState State { get; private set; } = QemuProcessState.NotStarted;

    /// <summary>The exit code, once <see cref="State"/> is <see cref="QemuProcessState.Exited"/>.</summary>
    public int? ExitCode { get; private set; }

    /// <summary>The per-VM log file path.</summary>
    public string LogFilePath { get; }

    /// <summary>Raised when the process exits.</summary>
    public event EventHandler? Exited;

    /// <summary>Spawns QEMU and begins capturing its output to the log file.</summary>
    public void Start()
    {
        if (State != QemuProcessState.NotStarted)
        {
            throw new InvalidOperationException("The QEMU process has already been started.");
        }

        // Per-VM log of combined stdout/stderr (overwritten each launch).
        _log = new StreamWriter(LogFilePath, append: false) { AutoFlush = true };
        WriteLaunchHeader();

        IRunningProcess process = _launcher.Start(new ProcessLaunchRequest
        {
            Executable = _executable,
            Arguments = _arguments,
            WorkingDirectory = _workingDirectory,
        });
        _process = process;
        process.OutputReceived += OnOutput;
        process.Exited += OnExited;
        State = QemuProcessState.Running;
    }

    /// <summary>Waits for the process to exit.</summary>
    public Task WaitForExitAsync(CancellationToken cancellationToken = default) =>
        _process?.WaitForExitAsync(cancellationToken) ?? Task.CompletedTask;

    /// <summary>Forcibly terminates the process (stops the VM).</summary>
    public void Kill() => _process?.Kill();

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_process is not null)
        {
            _process.OutputReceived -= OnOutput;
            _process.Exited -= OnExited;
            _process.Dispose();
        }

        CloseLog();
    }

    // Echo what Boxwright launched at the top of the log — human-readable, NOT shell-escaped
    // (args containing spaces aren't quoted), so a launch failure (e.g. a bad -cpu under WHPX)
    // is diagnosable alongside QEMU's own output below. Runs before the process starts, so no
    // OutputReceived can race it.
    private void WriteLaunchHeader()
    {
        lock (_logGate)
        {
            _log!.WriteLine($"=== Boxwright launch {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z ===");
            if (_accelerator is not null)
            {
                _log.WriteLine($"Accelerator: {_accelerator}");
            }

            _log.WriteLine($"Command: {_executable} {string.Join(' ', _arguments)}");
            _log.WriteLine("--- QEMU output ---");
        }
    }

    private void OnOutput(object? sender, string line)
    {
        lock (_logGate)
        {
            _log?.WriteLine(line);
        }
    }

    private void OnExited(object? sender, EventArgs e)
    {
        ExitCode = _process is { HasExited: true } ? _process.ExitCode : null;
        State = QemuProcessState.Exited;
        CloseLog();
        Exited?.Invoke(this, EventArgs.Empty);
    }

    private void CloseLog()
    {
        lock (_logGate)
        {
            _log?.Dispose();
            _log = null;
        }
    }
}

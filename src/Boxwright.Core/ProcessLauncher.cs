using System.Diagnostics;

namespace Boxwright.Core;

/// <summary>The default <see cref="IProcessLauncher"/>, backed by <see cref="Process"/>.</summary>
public sealed class ProcessLauncher : IProcessLauncher
{
    /// <inheritdoc />
    public IRunningProcess Start(ProcessLaunchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Executable);
        ArgumentNullException.ThrowIfNull(request.Arguments);

        var startInfo = new ProcessStartInfo
        {
            FileName = request.Executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        if (!string.IsNullOrEmpty(request.WorkingDirectory))
        {
            startInfo.WorkingDirectory = request.WorkingDirectory;
        }

        foreach (string argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return RealRunningProcess.Start(startInfo);
    }

    /// <inheritdoc />
    public IRunningProcess? Attach(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);

            // PID-reuse guard: only adopt an actual QEMU process (the OS may have recycled the id).
            if (!process.ProcessName.StartsWith("qemu-system", StringComparison.OrdinalIgnoreCase))
            {
                process.Dispose();
                return null;
            }

            return new AttachedProcess(process);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return null; // no process with that id
        }
    }

    private sealed class RealRunningProcess : IRunningProcess
    {
        private readonly Process _process;

        private RealRunningProcess(Process process) => _process = process;

        public int Id => _process.Id;

        public bool HasExited => _process.HasExited;

        public int ExitCode => _process.ExitCode;

        public event EventHandler<string>? OutputReceived;

        public event EventHandler? Exited;

        public static RealRunningProcess Start(ProcessStartInfo startInfo)
        {
            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            var running = new RealRunningProcess(process);
            process.OutputDataReceived += running.OnOutput;
            process.ErrorDataReceived += running.OnOutput;
            process.Exited += running.OnExited;

            _ = process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return running;
        }

        public Task WaitForExitAsync(CancellationToken cancellationToken = default) => _process.WaitForExitAsync(cancellationToken);

        public void Kill()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // Already exited.
            }
        }

        public void Dispose() => _process.Dispose();

        private void OnOutput(object? sender, DataReceivedEventArgs e)
        {
            if (e.Data is not null)
            {
                OutputReceived?.Invoke(this, e.Data);
            }
        }

        private void OnExited(object? sender, EventArgs e) => Exited?.Invoke(this, EventArgs.Empty);
    }

    // An IRunningProcess over a process we did NOT spawn (reconnect-on-restart, ADR-0014). Its stdout
    // can't be recovered (the original pipe died with the launching process), and a non-child process
    // can't be waited on by event cross-platform — so exit is detected by polling liveness on a timer.
    private sealed class AttachedProcess : IRunningProcess
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

        private readonly Process _process;
        private readonly System.Threading.Timer _poll;
        private int _exitRaised;

        public AttachedProcess(Process process)
        {
            _process = process;
            _poll = new System.Threading.Timer(_ => CheckExited(), state: null, PollInterval, PollInterval);
        }

        public int Id => _process.Id;

        public bool HasExited
        {
            get
            {
                try
                {
                    return _process.HasExited;
                }
                catch (InvalidOperationException)
                {
                    return true;
                }
            }
        }

        public int ExitCode
        {
            get
            {
                try
                {
                    return _process.ExitCode;
                }
                catch (InvalidOperationException)
                {
                    return 0;
                }
            }
        }

#pragma warning disable CS0067 // Never raised: an adopted process has no readable stdout/stderr pipe.
        public event EventHandler<string>? OutputReceived;
#pragma warning restore CS0067

        public event EventHandler? Exited;

        public Task WaitForExitAsync(CancellationToken cancellationToken = default) => _process.WaitForExitAsync(cancellationToken);

        public void Kill()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // Already exited.
            }
        }

        public void Dispose()
        {
            _poll.Dispose();
            _process.Dispose();
        }

        private void CheckExited()
        {
            if (!HasExited || Interlocked.Exchange(ref _exitRaised, 1) != 0)
            {
                return;
            }

            _poll.Dispose();
            Exited?.Invoke(this, EventArgs.Empty);
        }
    }
}

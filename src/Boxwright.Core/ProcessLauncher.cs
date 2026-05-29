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
}

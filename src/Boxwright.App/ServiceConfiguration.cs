using Boxwright.App.Services;
using Boxwright.App.ViewModels;
using Boxwright.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Boxwright.App;

/// <summary>
/// The composition root: registers Core services and view models in one place so
/// DI wiring stays out of <see cref="App"/> and later APP milestones have a single
/// seam to extend. Registration only — no QEMU/process logic lives here.
/// </summary>
internal static class ServiceConfiguration
{
    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // App-wide structured logging: a rolling file (app diagnostics) plus Debug output.
        // Lower the minimum level to Debug to also capture QMP traffic and full command lines.
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddProvider(new FileLoggerProvider(AppPaths.AppLogFile));
            builder.AddDebug();
        });

        // Core infrastructure (process, sockets, tool discovery). The optional
        // "bundled directory" arguments are null in dev: tools resolve from PATH.
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IProcessLauncher, ProcessLauncher>();
        services.AddSingleton<IEndpointAllocator, QmpEndpointAllocator>();
        services.AddSingleton<IQmpConnector, DefaultQmpConnector>();
        services.AddSingleton<IRemoteViewerLocator>(_ => new RemoteViewerLocator());
        services.AddSingleton(_ => new QemuLocator());
        services.AddSingleton(_ => AcceleratorDetector.CreateDefault());

        // Core services (orchestration).
        services.AddSingleton<IDiskService, DiskService>();
        services.AddSingleton<IDisplayLauncher, DisplayLauncher>();
        services.AddSingleton<IVmLauncher, VmLauncher>();
        services.AddSingleton(_ => new VmRepository(VmRepository.DefaultRootDirectory));

        // UI-thread marshalling for background callbacks (VM process exit).
        services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();

        // File selection (installer ISOs) via Avalonia's storage provider.
        services.AddSingleton<IFilePicker, AvaloniaFilePicker>();

        // Reads per-VM qemu.log for the Logs panel.
        services.AddSingleton<ILogReader, FileLogReader>();

        // View models.
        services.AddTransient<VmListViewModel>();
        services.AddTransient<MainWindowViewModel>();
    }
}

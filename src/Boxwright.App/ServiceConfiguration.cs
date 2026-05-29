using Boxwright.App.ViewModels;
using Boxwright.Core;
using Microsoft.Extensions.DependencyInjection;

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
        services.AddSingleton<DiskService>();
        services.AddSingleton<DisplayLauncher>();
        services.AddSingleton<VmLauncher>();
        services.AddSingleton(_ => new VmRepository(VmRepository.DefaultRootDirectory));

        // View models.
        services.AddTransient<MainWindowViewModel>();
    }
}

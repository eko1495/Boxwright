using Boxwright.Cli.Commands;
using Boxwright.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Boxwright.Cli;

/// <summary>
/// The CLI composition root: registers the Core orchestration services (the same ones the App
/// wires up, minus everything Avalonia) plus the CLI's commands and helpers. Registration only —
/// no QEMU/process logic lives here (Directive 8). Mirrors
/// <c>Boxwright.App.ServiceConfiguration</c> so both front ends drive identical Core behavior.
/// </summary>
internal static class CliServices
{
    /// <summary>Environment variable that overrides the VMs root (e.g. for tests or a shared store).</summary>
    public const string VmsDirectoryEnvVar = "BOXWRIGHT_VMS_DIR";

    /// <summary>Builds the configured service provider for the CLI.</summary>
    public static ServiceProvider Build(CliOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var services = new ServiceCollection();
        services.AddSingleton(output);

        // Logging: registered so ILogger<T> dependencies resolve, but with no provider the CLI stays
        // quiet — commands speak to the user through CliOutput, not the log. BOXWRIGHT_LOG_LEVEL could
        // later attach a console provider without touching call sites.
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));

        // Core infrastructure (process, sockets, tool discovery). QemuLocator points at a "qemu" folder
        // beside the executable (packaged layout, ADR-0009); inert in dev, where QEMU resolves from PATH.
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IProcessLauncher, ProcessLauncher>();
        services.AddSingleton<IEndpointAllocator, QmpEndpointAllocator>();
        services.AddSingleton<IQmpConnector, DefaultQmpConnector>();
        services.AddSingleton<IQgaConnector, DefaultQgaConnector>();
        services.AddSingleton<IRemoteViewerLocator>(_ => new RemoteViewerLocator());
        services.AddSingleton(_ => new QemuLocator(Path.Combine(AppContext.BaseDirectory, "qemu")));
        services.AddSingleton(_ => AcceleratorDetector.CreateDefault());

        // Core services (orchestration).
        services.AddSingleton<IDiskService, DiskService>();
        services.AddSingleton<ISnapshotService, SnapshotService>();
        services.AddSingleton<IVmSnapshotService, VmSnapshotService>();
        services.AddSingleton<IVmCloneService, VmCloneService>();
        services.AddSingleton<IVmDeletionService, VmDeletionService>();
        services.AddSingleton<IVmRenameService, VmRenameService>();
        services.AddSingleton<IVmDiskUsageService, VmDiskUsageService>();
        services.AddSingleton<IVmIntegrityService, VmIntegrityService>();
        services.AddSingleton<IDisplayLauncher, DisplayLauncher>();
        services.AddSingleton<IVmLauncher, VmLauncher>();
        services.AddSingleton<IVmRuntimeStore, VmRuntimeStore>();
        services.AddSingleton(sp => new VmRepository(ResolveVmsRoot(), sp.GetService<ILogger<VmRepository>>()));

        // OS catalog ("Get an OS"): remote manifest wrapping the bundled list as the offline fallback
        // (ADR-0020). One shared HttpClient. A network failure degrades to the bundled catalog.
        services.AddSingleton(_ => new HttpClient());
        services.AddSingleton<IHttpStreamSource, HttpClientStreamSource>();
        services.AddSingleton<BundledOsCatalogSource>();
        // Local community recipes (ADR-0026) extend the catalog from a folder of *.json files.
        services.AddSingleton(sp => new LocalRecipeCatalogSource(
            LocalRecipeCatalogSource.DefaultDirectory, sp.GetService<ILogger<LocalRecipeCatalogSource>>()));
        // The catalog: remote (→ cache → bundled, ADR-0020) with local recipes layered on top (they win by id).
        services.AddSingleton<IOsCatalogSource>(sp => new CompositeOsCatalogSource(
            [
                new RemoteOsCatalogSource(
                    sp.GetRequiredService<IHttpStreamSource>(),
                    sp.GetRequiredService<BundledOsCatalogSource>(),
                    new Uri(RemoteOsCatalogSource.DefaultCatalogUrl),
                    RemoteOsCatalogSource.DefaultCacheFilePath,
                    TimeSpan.FromSeconds(5),
                    sp.GetService<ILogger<RemoteOsCatalogSource>>()),
                sp.GetRequiredService<LocalRecipeCatalogSource>(),
            ],
            sp.GetService<ILogger<CompositeOsCatalogSource>>()));
        // OpenPGP provenance gate for catalog downloads (ADR-0027): a pure-managed verifier plus the
        // bundled trusted-key store. Entries without a signature block are unaffected (SHA-256 only).
        services.AddSingleton<IOpenPgpVerifier, OpenPgpVerifier>();
        services.AddSingleton<ITrustedKeyProvider, BundledTrustedKeyProvider>();
        services.AddSingleton<IIsoDownloader>(sp =>
            new IsoDownloader(
                sp.GetRequiredService<IHttpStreamSource>(),
                IsoDownloader.DefaultCacheDirectory,
                sp.GetRequiredService<IOpenPgpVerifier>(),
                sp.GetRequiredService<ITrustedKeyProvider>()));

        // Unattended-install seed generators + the per-family installer resolver (ADR-0013/0016/0017),
        // and the catalog-VM orchestration that ties download + disk + seed together (ADR-0022) so
        // 'create --os <id>' runs the same path as the GUI's New-VM flow.
        services.AddSingleton<ISeedGenerator, CloudInitSeedGenerator>();
        services.AddSingleton<IInstallMediaExtractor, InstallMediaExtractor>();
        // Ubuntu still uses a bespoke installer (its casper/layerfs grub.cfg introspection needs code);
        // Debian + Fedora are now declarative recipes in the bundled catalog (ADR-0026), routed through
        // RecipeInstaller, so their per-family installers were deleted.
        services.AddSingleton<IUnattendedInstaller, UbuntuAutoinstaller>();
        services.AddSingleton<IUnattendedInstallerResolver, UnattendedInstallerResolver>();
        services.AddSingleton<IRecipeInstaller, RecipeInstaller>();
        services.AddSingleton<ICatalogVmInstaller, CatalogVmInstaller>();

        // Host USB enumeration for the passthrough picker (ADR-0023): Linux sysfs, capability-gated elsewhere.
        services.AddSingleton(sp => UsbDeviceEnumerator.CreateDefault(sp.GetRequiredService<IProcessRunner>()));

        // CLI helpers.
        services.AddSingleton<VmResolver>();
        services.AddSingleton<IVmStatusProbe, VmStatusProbe>();

        // Commands. Each is also exposed as ICliCommand for the dispatcher to enumerate.
        AddCommand<ListCommand>(services);
        AddCommand<InfoCommand>(services);
        AddCommand<CreateCommand>(services);
        AddCommand<SetCommand>(services);
        AddCommand<UsbCommand>(services);
        AddCommand<NetCommand>(services);
        AddCommand<TemplateCommand>(services);
        AddCommand<RecipeCommand>(services);
        AddCommand<CloneCommand>(services);
        AddCommand<StartCommand>(services);
        AddCommand<StopCommand>(services);
        AddCommand<DisplayCommand>(services);
        AddCommand<DeleteCommand>(services);
        AddCommand<RenameCommand>(services);
        AddCommand<OsCommand>(services);
        AddCommand<SnapshotCommand>(services);
        AddCommand<CheckCommand>(services);

        services.AddSingleton<CommandDispatcher>();

        return services.BuildServiceProvider();
    }

    private static void AddCommand<TCommand>(IServiceCollection services)
        where TCommand : class, ICliCommand =>
        services.AddSingleton<ICliCommand, TCommand>();

    private static string ResolveVmsRoot()
    {
        string? overridden = Environment.GetEnvironmentVariable(VmsDirectoryEnvVar);
        return string.IsNullOrWhiteSpace(overridden) ? VmRepository.DefaultRootDirectory : overridden;
    }
}

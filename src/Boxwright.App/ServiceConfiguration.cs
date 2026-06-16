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
        // Defaults to Information (keeps the file small); set BOXWRIGHT_LOG_LEVEL=Debug (or
        // Trace) to also capture QMP traffic and full command lines — in a packaged build,
        // without recompiling.
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(ResolveMinimumLevel(Environment.GetEnvironmentVariable("BOXWRIGHT_LOG_LEVEL")));
            builder.AddProvider(new FileLoggerProvider(AppPaths.AppLogFile));
            builder.AddDebug();
        });

        // Core infrastructure (process, sockets, tool discovery). QemuLocator is pointed at a
        // "qemu" folder beside the executable — the packaged layout (ADR-0009). It's inert when
        // that folder is absent (dev), where QEMU resolves from PATH. remote-viewer is NOT
        // bundled (ADR-0008) and resolves from PATH / Program Files.
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
        services.AddSingleton<ILiveSnapshotService, LiveSnapshotService>();
        services.AddSingleton<IVmCloneService, VmCloneService>();
        services.AddSingleton<IVmDeletionService, VmDeletionService>();
        // GUI rename is deferred (ADR-0028 phase 2), but the service is registered here to keep the two
        // composition roots mirrored (ADR-0022) so a future viewmodel can adopt it with no Core change.
        services.AddSingleton<IVmRenameService, VmRenameService>();
        services.AddSingleton<IVmDiskUsageService, VmDiskUsageService>();
        services.AddSingleton<IVmIntegrityService, VmIntegrityService>();
        services.AddSingleton<IDisplayLauncher, DisplayLauncher>();
        services.AddSingleton<IVmLauncher, VmLauncher>();
        services.AddSingleton(sp => new VmRepository(VmRepository.DefaultRootDirectory, sp.GetService<ILogger<VmRepository>>()));

        // Per-VM runtime state (runtime.json) so the app can re-adopt running QEMU after a restart (ADR-0014).
        services.AddSingleton<IVmRuntimeStore, VmRuntimeStore>();

        // OS catalog + ISO downloader ("Get an OS"). One shared HttpClient — never per-call.
        services.AddSingleton(_ => new HttpClient());
        services.AddSingleton<IHttpStreamSource, HttpClientStreamSource>();

        // Catalog source: the remote (community-maintainable) manifest, wrapping the bundled list as the
        // offline fallback — remote → last-good cache → bundled, best-effort (ADR-0020). Default-on; a
        // network failure degrades silently so the catalog UI always gets a usable list.
        services.AddSingleton<BundledOsCatalogSource>();
        // Local community recipes (ADR-0026) extend the catalog from a folder of *.json files.
        services.AddSingleton(sp => new LocalRecipeCatalogSource(
            LocalRecipeCatalogSource.DefaultDirectory, sp.GetService<ILogger<LocalRecipeCatalogSource>>()));
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

        // UI-thread marshalling for background callbacks (VM process exit).
        services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();

        // File selection (installer ISOs) via Avalonia's storage provider.
        services.AddSingleton<IFilePicker, AvaloniaFilePicker>();

        // Reads per-VM qemu.log for the Logs panel.
        services.AddSingleton<ILogReader, FileLogReader>();

        // Reveals the app-wide logs folder in the OS file manager (toolbar button).
        services.AddSingleton<IFolderOpener, ShellFolderOpener>();

        // Embedded VNC display: renders a VNC VM's screen in-app (MarcusW.VncClient.Avalonia).
        services.AddSingleton<IEmbeddedVncDisplay, AvaloniaVncDisplay>();

        // Unattended-install seed generator: writes a cloud-init NoCloud CIDATA image (Ubuntu autoinstall).
        services.AddSingleton<ISeedGenerator, CloudInitSeedGenerator>();

        // Windows unattended-install seed: an Autounattend.xml ISO that Setup auto-discovers (ADR-0015).
        services.AddSingleton<IAutounattendSeedGenerator, AutounattendSeedGenerator>();

        // Extracts an installer ISO's kernel/initrd so an Ubuntu autoinstall boots hands-free (ADR-0013 Phase B).
        services.AddSingleton<IInstallMediaExtractor, InstallMediaExtractor>();

        // Per-family unattended installers, resolved by OS family (ADR-0016): Ubuntu autoinstall (cloud-init
        // CIDATA seed) and Debian preseed (initrd-injected). New families are just one more registration here.
        // Ubuntu still uses a bespoke installer (its casper/layerfs grub.cfg introspection needs code);
        // Debian + Fedora are now declarative recipes in the bundled catalog (ADR-0026), routed through
        // RecipeInstaller, so their per-family installers were deleted.
        services.AddSingleton<IUnattendedInstaller, UbuntuAutoinstaller>();
        services.AddSingleton<IUnattendedInstallerResolver, UnattendedInstallerResolver>();
        services.AddSingleton<IRecipeInstaller, RecipeInstaller>();

        // The catalog-VM orchestration (download + disk + seed) shared by the GUI's "Get an OS" flow and
        // the headless CLI (ADR-0022). CatalogViewModel delegates to this rather than re-implementing it.
        services.AddSingleton<ICatalogVmInstaller, CatalogVmInstaller>();

        // Host USB enumeration for the passthrough picker (ADR-0023): Linux sysfs / macOS system_profiler /
        // Windows SetupAPI, capability-gated. Needs the process runner (for the macOS system_profiler call).
        services.AddSingleton(sp => UsbDeviceEnumerator.CreateDefault(sp.GetRequiredService<IProcessRunner>()));

        // View models.
        services.AddTransient<VmListViewModel>();
        services.AddTransient<MainWindowViewModel>();
    }

    /// <summary>
    /// Resolves the logging floor from a <paramref name="configured"/> level name (the
    /// <c>BOXWRIGHT_LOG_LEVEL</c> environment variable; case-insensitive, e.g. <c>Debug</c>/<c>Trace</c>),
    /// defaulting to <see cref="LogLevel.Information"/> when unset or unrecognized. Lets a shipped
    /// build surface QMP traffic and full command lines without a rebuild.
    /// </summary>
    internal static LogLevel ResolveMinimumLevel(string? configured) =>
        Enum.TryParse(configured, ignoreCase: true, out LogLevel level) && Enum.IsDefined(level)
            ? level
            : LogLevel.Information;
}

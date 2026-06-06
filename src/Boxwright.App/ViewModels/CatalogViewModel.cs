using System.Collections.ObjectModel;
using Boxwright.App.Services;
using Boxwright.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Boxwright.App.ViewModels;

/// <summary>
/// The "Get an OS" gallery: lists catalog entries, prefills recommended specs for the
/// selected one (name editable), then downloads its ISO (verified, with progress and
/// cancel) and creates a VM with the ISO attached and CD-first boot. The VM-create step
/// reuses the same create-then-disk-with-rollback flow as <see cref="NewVmViewModel"/>;
/// the download is verified by <see cref="IIsoDownloader"/>. UI-free, so it is unit-testable.
/// </summary>
public sealed partial class CatalogViewModel : ObservableObject, IDisposable
{
    private const long BytesPerGiB = 1024L * 1024 * 1024;

    private readonly IOsCatalogSource _catalogSource;
    private readonly IIsoDownloader _downloader;
    private readonly VmRepository _repository;
    private readonly IDiskService _diskService;
    private readonly ISeedGenerator _seedGenerator;
    private readonly IUnattendedInstallerResolver _installerResolver;
    private readonly IUiDispatcher _dispatcher;
    private readonly Func<string, bool> _isNameTaken;
    private CancellationTokenSource? _cts;

    public CatalogViewModel(
        IOsCatalogSource catalogSource,
        IIsoDownloader downloader,
        VmRepository repository,
        IDiskService diskService,
        ISeedGenerator seedGenerator,
        IUnattendedInstallerResolver installerResolver,
        IUiDispatcher dispatcher,
        Func<string, bool> isNameTaken)
    {
        ArgumentNullException.ThrowIfNull(catalogSource);
        ArgumentNullException.ThrowIfNull(downloader);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(diskService);
        ArgumentNullException.ThrowIfNull(seedGenerator);
        ArgumentNullException.ThrowIfNull(installerResolver);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(isNameTaken);

        _catalogSource = catalogSource;
        _downloader = downloader;
        _repository = repository;
        _diskService = diskService;
        _seedGenerator = seedGenerator;
        _installerResolver = installerResolver;
        _dispatcher = dispatcher;
        _isNameTaken = isNameTaken;
    }

    /// <summary>The available OS catalog entries (loaded via <see cref="LoadEntriesCommand"/>).</summary>
    public ObservableCollection<OsCatalogEntry> Entries { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(ProvenanceText), nameof(HasNotes),
        nameof(NotesText), nameof(RequiresLicense), nameof(LicenseText),
        nameof(SelectedSupportsUnattended), nameof(ShowManualInstallNote),
        nameof(IsCloudImage), nameof(ShowAutoinstallOptIn),
        nameof(ValidationError), nameof(HasValidationError))]
    [NotifyCanExecuteChangedFor(nameof(GetItCommand))]
    private OsCatalogEntry? _selectedEntry;

    // Prefill the confirm fields from the selected OS's recommended specs.
    partial void OnSelectedEntryChanged(OsCatalogEntry? value)
    {
        if (value is null)
        {
            return;
        }

        Name = value.Name;
        MemoryMiB = value.Recommended.MemoryMiB;
        CpuCores = value.Recommended.CpuCores;
        DiskSizeGiB = value.Recommended.DiskGiB;
        Firmware = value.Recommended.Firmware;
        ErrorMessage = null;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError), nameof(HasValidationError))]
    [NotifyCanExecuteChangedFor(nameof(GetItCommand))]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError), nameof(HasValidationError))]
    [NotifyCanExecuteChangedFor(nameof(GetItCommand))]
    private int _memoryMiB = 2048;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError), nameof(HasValidationError))]
    [NotifyCanExecuteChangedFor(nameof(GetItCommand))]
    private int _cpuCores = 2;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError), nameof(HasValidationError))]
    [NotifyCanExecuteChangedFor(nameof(GetItCommand))]
    private int _diskSizeGiB = 20;

    [ObservableProperty]
    private string _firmware = "uefi";

    /// <summary>
    /// Whether to set up an unattended (autoinstall) install for the selected OS. Opt-in/off by default.
    /// When on, Boxwright bakes the cloud-init seed and boots the installer with the <c>autoinstall</c>
    /// kernel arg (ADR-0013 Phase B), so the install runs fully hands-free.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError), nameof(HasValidationError))]
    [NotifyCanExecuteChangedFor(nameof(GetItCommand))]
    private bool _unattendedEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError), nameof(HasValidationError))]
    [NotifyCanExecuteChangedFor(nameof(GetItCommand))]
    private string _hostname = "ubuntu";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError), nameof(HasValidationError))]
    [NotifyCanExecuteChangedFor(nameof(GetItCommand))]
    private string _unattendedUsername = "ubuntu";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError), nameof(HasValidationError))]
    [NotifyCanExecuteChangedFor(nameof(GetItCommand))]
    private string _unattendedPassword = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrorMessage))]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GetItCommand), nameof(CancelDownloadCommand))]
    private bool _isDownloading;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string? _progressText;

    /// <summary>The firmware choices offered (matches <see cref="NewVmViewModel"/>).</summary>
    public IReadOnlyList<string> FirmwareOptions { get; } = ["bios", "uefi"];

    /// <summary>True once an OS is selected (reveals the confirm panel).</summary>
    public bool HasSelection => SelectedEntry is not null;

    /// <summary>Provenance + size + verification line shown for the selected OS.</summary>
    public string? ProvenanceText => SelectedEntry is { } e
        ? $"From {e.SourceName} · download ~{Humanize(e.SizeBytes)} · verified with SHA-256"
        : null;

    /// <summary>True when the selected OS has an informational note (and isn't license-gated).</summary>
    public bool HasNotes => SelectedEntry is { RequiresLicense: false, Notes: not null and not "" };

    /// <summary>The selected OS's informational note, if any.</summary>
    public string? NotesText => SelectedEntry?.Notes;

    /// <summary>True when the selected OS needs a license the user must supply (e.g. a Windows evaluation).</summary>
    public bool RequiresLicense => SelectedEntry?.RequiresLicense ?? false;

    /// <summary>The license warning shown for a license-gated OS.</summary>
    public string? LicenseText => RequiresLicense
        ? $"This OS needs a license you must provide. {SelectedEntry?.Notes}".Trim()
        : null;

    /// <summary>True when the selected OS supports unattended install (currently Ubuntu autoinstall).</summary>
    public bool SelectedSupportsUnattended => SelectedEntry?.SupportsAutoinstall ?? false;

    /// <summary>True when an OS is selected but can't be installed unattended (show a manual-install note).</summary>
    public bool ShowManualInstallNote => HasSelection && !SelectedSupportsUnattended;

    /// <summary>True when the selected entry is a pre-installed cloud image (vs. an installer ISO).</summary>
    public bool IsCloudImage => SelectedEntry?.ImageKind == OsCatalogEntry.ImageKindCloudImage;

    /// <summary>
    /// True when the selected OS shows the experimental autoinstall opt-in — i.e. it supports
    /// unattended install AND is an installer ISO. A cloud image instead requires credentials (the
    /// seed is the guest's only login), so it shows a required-credentials panel, not the opt-in.
    /// </summary>
    public bool ShowAutoinstallOptIn => SelectedSupportsUnattended && !IsCloudImage;

    // Whether a seed should actually be baked for the current selection. Always for a cloud image
    // (its login lives only in the seed); for an installer ISO, only when the user opts in.
    private bool UnattendedActive => IsCloudImage || (SelectedSupportsUnattended && UnattendedEnabled);

    /// <summary>The first validation problem with the confirm fields, or null when valid.</summary>
    public string? ValidationError
    {
        get
        {
            if (SelectedEntry is null)
            {
                return null; // nothing to validate until an OS is picked
            }

            if (string.IsNullOrWhiteSpace(Name))
            {
                return "Enter a name for the VM.";
            }

            if (_isNameTaken(Name.Trim()))
            {
                return $"A VM named “{Name.Trim()}” already exists.";
            }

            if (MemoryMiB <= 0)
            {
                return "Memory must be greater than 0 MiB.";
            }

            if (CpuCores <= 0)
            {
                return "CPU cores must be greater than 0.";
            }

            if (DiskSizeGiB <= 0)
            {
                return "Disk size must be greater than 0 GiB.";
            }

            if (UnattendedActive)
            {
                if (string.IsNullOrWhiteSpace(Hostname))
                {
                    return "Enter a hostname for the guest.";
                }

                if (string.IsNullOrWhiteSpace(UnattendedUsername))
                {
                    return "Enter a username for the guest.";
                }

                if (string.IsNullOrEmpty(UnattendedPassword))
                {
                    return "Set a password for the guest.";
                }
            }

            return null;
        }
    }

    public bool HasValidationError => ValidationError is not null;

    public bool HasErrorMessage => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>Raised with the created VM once its folder, config, and disk all exist.</summary>
    public event EventHandler<Vm>? Created;

    /// <summary>Raised when the user dismisses the gallery without creating.</summary>
    public event EventHandler? Cancelled;

    [RelayCommand]
    private async Task LoadEntriesAsync()
    {
        ErrorMessage = null;
        try
        {
            IReadOnlyList<OsCatalogEntry> loaded = await _catalogSource.GetEntriesAsync();
            Entries.Clear();
            foreach (OsCatalogEntry entry in loaded)
            {
                Entries.Add(entry);
            }
        }
        catch (OsCatalogException ex)
        {
            ErrorMessage = $"Couldn't load the OS catalog: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanGetIt))]
    private async Task GetItAsync()
    {
        if (SelectedEntry is not { } entry)
        {
            return;
        }

        IsDownloading = true;
        ErrorMessage = null;
        ProgressPercent = 0;
        ProgressText = null;
        _cts = new CancellationTokenSource();

        string downloadedPath;
        try
        {
            var progress = new DispatchedProgress(_dispatcher, OnProgress);
            downloadedPath = await _downloader.EnsureAsync(entry, progress, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            ResetDownloadState();
            return; // deliberate cancel — no message, no VM
        }
        catch (DownloadException ex)
        {
            ErrorMessage = ex.Message;
            ResetDownloadState();
            return;
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }

        // A cloud image is a pre-installed disk, not an installer — it follows a different create flow.
        if (IsCloudImage)
        {
            await CreateFromCloudImageAsync(downloadedPath);
            return;
        }

        // ISO is downloaded and verified — create the VM (same flow as NewVmViewModel).
        Vm vm;
        try
        {
            vm = await _repository.CreateAsync(BuildConfig(downloadedPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = $"Couldn't create the VM: {ex.Message}";
            ResetDownloadState();
            return;
        }

        // Unattended install: the per-family installer (resolved by OS family — ADR-0016) prepares the
        // installer media and returns how to boot it. For Ubuntu that's a cloud-init seed disk plus an
        // `autoinstall` kernel boot; for Debian it's a preseed injected into the installer initrd. The
        // one-shot InstallBoot is what makes the installer skip the manual disk-erase confirmation; it is
        // cleared automatically once the install finishes (see VmListItemViewModel.OnSessionExited).
        if (UnattendedActive)
        {
            try
            {
                UnattendedInstallPlan plan = _installerResolver.Resolve(entry.OsFamily).Prepare(downloadedPath, vm.FolderPath, BuildAnswers());
                VmConfig withSeed = vm.Config with
                {
                    Disks = [.. vm.Config.Disks, .. plan.SeedDisks],
                    InstallBoot = plan.Boot,
                };
                await _repository.SaveAsync(withSeed);
                vm = vm with { Config = withSeed };
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InstallMediaException)
            {
                await _repository.DeleteAsync(vm.Config.Id);
                ErrorMessage = $"Couldn't set up the unattended install: {ex.Message}";
                ResetDownloadState();
                return;
            }
        }

        try
        {
            string diskPath = Path.Combine(vm.FolderPath, NewVmViewModel.DiskFileName);
            await _diskService.CreateAsync(diskPath, (long)DiskSizeGiB * BytesPerGiB);
        }
        catch (DiskException ex)
        {
            // Roll back so the list never shows a VM without its disk.
            await _repository.DeleteAsync(vm.Config.Id);
            ErrorMessage = $"Couldn't create the disk: {ex.Message}";
            ResetDownloadState();
            return;
        }

        ResetDownloadState();
        Created?.Invoke(this, vm);
    }

    // Cloud-image flow: the download is a pre-installed qcow2. Flatten it into the VM folder as the
    // disk (keeping the folder self-contained/portable — ADR-0006), grow it to the requested size,
    // then attach the cloud-init seed that sets up the login (required — the image has no default one).
    private async Task CreateFromCloudImageAsync(string imagePath)
    {
        Vm vm;
        try
        {
            vm = await _repository.CreateAsync(BuildCloudImageConfig());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = $"Couldn't create the VM: {ex.Message}";
            ResetDownloadState();
            return;
        }

        try
        {
            string diskPath = Path.Combine(vm.FolderPath, NewVmViewModel.DiskFileName);
            await _diskService.CopyAsync(imagePath, diskPath);

            // Only grow — never shrink below the image's own virtual size (qemu-img would refuse).
            long requestedBytes = (long)DiskSizeGiB * BytesPerGiB;
            DiskInfo info = await _diskService.GetInfoAsync(diskPath);
            if (requestedBytes > info.VirtualSize)
            {
                await _diskService.ResizeAsync(diskPath, requestedBytes);
            }
        }
        catch (DiskException ex)
        {
            await _repository.DeleteAsync(vm.Config.Id); // roll back the half-prepared VM
            ErrorMessage = $"Couldn't prepare the cloud image: {ex.Message}";
            ResetDownloadState();
            return;
        }

        try
        {
            _seedGenerator.Generate(BuildAnswers(), vm.FolderPath, SeedProfile.CloudImage);
            VmConfig withSeed = vm.Config with
            {
                Disks = [.. vm.Config.Disks, new DiskConfig { File = CloudInitSeedGenerator.SeedFileName, Format = "raw", Interface = "virtio" }],
            };
            await _repository.SaveAsync(withSeed);
            vm = vm with { Config = withSeed };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _repository.DeleteAsync(vm.Config.Id);
            ErrorMessage = $"Couldn't create the cloud-init seed: {ex.Message}";
            ResetDownloadState();
            return;
        }

        ResetDownloadState();
        Created?.Invoke(this, vm);
    }

    private bool CanGetIt() => !IsDownloading && HasSelection && ValidationError is null;

    [RelayCommand(CanExecute = nameof(CanCancelDownload))]
    private void CancelDownload() => _cts?.Cancel();

    private bool CanCancelDownload() => IsDownloading;

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke(this, EventArgs.Empty);

    /// <summary>Releases the cancellation source of any in-flight download.</summary>
    public void Dispose()
    {
        _cts?.Dispose();
        _cts = null;
    }

    private void OnProgress(IsoDownloadProgress progress)
    {
        ProgressPercent = progress.Percent ?? 0;
        ProgressText = progress.TotalBytes is > 0
            ? $"{Humanize(progress.BytesReceived)} / {Humanize(progress.TotalBytes.Value)}"
            : Humanize(progress.BytesReceived);
    }

    private void ResetDownloadState()
    {
        IsDownloading = false;
        ProgressText = null;
        ProgressPercent = 0;
    }

    private UnattendedAnswers BuildAnswers() => new()
    {
        Hostname = Hostname.Trim(),
        Username = UnattendedUsername.Trim(),
        Password = UnattendedPassword,
    };

    private VmConfig BuildConfig(string isoPath) => new()
    {
        Name = Name.Trim(),
        MemoryMiB = MemoryMiB,
        Cpu = new CpuConfig { Sockets = 1, Cores = CpuCores, Threads = 1 },
        Firmware = Firmware,
        OsType = "linux", // every catalog entry is a Linux distro today
        Disks = [new DiskConfig { File = NewVmViewModel.DiskFileName, Format = "qcow2", Interface = "virtio" }],
        RemovableMedia = [new RemovableMediaConfig { Type = "cdrom", File = isoPath, Attached = true }],
        Boot = new BootConfig { Order = "dc" }, // boot the installer first, then disk
    };

    // A cloud image is already installed onto its disk, so there's no installer media and the VM
    // boots straight from the disk.
    private VmConfig BuildCloudImageConfig() => new()
    {
        Name = Name.Trim(),
        MemoryMiB = MemoryMiB,
        Cpu = new CpuConfig { Sockets = 1, Cores = CpuCores, Threads = 1 },
        Firmware = Firmware,
        OsType = "linux",
        Disks = [new DiskConfig { File = NewVmViewModel.DiskFileName, Format = "qcow2", Interface = "virtio" }],
        Boot = new BootConfig { Order = "c" }, // boot the pre-installed disk; no installer media
    };

    private static string Humanize(long bytes)
    {
        const double gb = 1_000_000_000d;
        const double mb = 1_000_000d;
        return bytes >= gb ? $"{bytes / gb:0.0} GB" : $"{bytes / mb:0} MB";
    }
}

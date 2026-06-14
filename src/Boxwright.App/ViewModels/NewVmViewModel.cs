using Boxwright.App.Services;
using Boxwright.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Boxwright.App.ViewModels;

/// <summary>
/// The guided "New VM" form: collects name, memory, cores, disk size, and firmware
/// (with sane, overridable defaults), validates them, and on create writes the VM
/// folder + config (<see cref="VmRepository"/>) and a qcow2 disk (<see cref="IDiskService"/>).
/// A disk failure rolls back the half-created VM. When the guest is Windows it can also set up an
/// <b>unattended install</b> from a user-supplied ISO: it bakes an <c>Autounattend.xml</c> seed CD,
/// attaches it next to the Windows ISO, and puts storage on SATA (ADR-0015). UI-free, so it is unit-testable.
/// </summary>
public sealed partial class NewVmViewModel : ObservableObject, IDisposable
{
    /// <summary>The disk image file name created inside each new VM's folder.</summary>
    public const string DiskFileName = "disk.qcow2";

    private const long BytesPerGiB = 1024L * 1024 * 1024;

    private readonly VmRepository _repository;
    private readonly IDiskService _diskService;
    private readonly IFilePicker _filePicker;
    private readonly IAutounattendSeedGenerator _autounattendSeedGenerator;
    private readonly IIsoDownloader _downloader;
    private readonly IUiDispatcher _dispatcher;
    private readonly Func<string, bool> _isNameTaken;
    private CancellationTokenSource? _cts;

    public NewVmViewModel(
        VmRepository repository,
        IDiskService diskService,
        IFilePicker filePicker,
        IAutounattendSeedGenerator autounattendSeedGenerator,
        IIsoDownloader downloader,
        IUiDispatcher dispatcher,
        Func<string, bool> isNameTaken)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(diskService);
        ArgumentNullException.ThrowIfNull(filePicker);
        ArgumentNullException.ThrowIfNull(autounattendSeedGenerator);
        ArgumentNullException.ThrowIfNull(downloader);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(isNameTaken);

        _repository = repository;
        _diskService = diskService;
        _filePicker = filePicker;
        _autounattendSeedGenerator = autounattendSeedGenerator;
        _downloader = downloader;
        _dispatcher = dispatcher;
        _isNameTaken = isNameTaken;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError), nameof(HasValidationError))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError), nameof(HasValidationError))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private int _memoryMiB = 2048;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError), nameof(HasValidationError))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private int _cpuCores = 2;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError), nameof(HasValidationError))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private int _diskSizeGiB = 20;

    [ObservableProperty]
    private string _firmware = "bios";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWindows), nameof(ValidationError), nameof(HasValidationError))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _osType = "linux";

    // Windows 11 requires UEFI, so default to it when Windows is selected (the user can switch back to BIOS for Win10).
    partial void OnOsTypeChanged(string value)
    {
        if (string.Equals(value, "windows", StringComparison.OrdinalIgnoreCase))
        {
            Firmware = "uefi";
        }
    }

    /// <summary>Whether to set up an unattended Windows install (only meaningful when <see cref="IsWindows"/>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError), nameof(HasValidationError))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private bool _windowsUnattended;

    /// <summary>The user-supplied Windows installer ISO (for the unattended path).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError), nameof(HasValidationError), nameof(HasWindowsIso))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string? _windowsIsoPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError), nameof(HasValidationError))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _hostname = "windows-pc";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError), nameof(HasValidationError))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _unattendedUsername = "user";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError), nameof(HasValidationError))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _unattendedPassword = string.Empty;

    /// <summary>Optional product key — leave blank for an Evaluation or single-edition ISO.</summary>
    [ObservableProperty]
    private string _productKey = string.Empty;

    /// <summary>
    /// Use the faster paravirtualized <b>virtio</b> disk + NIC (ADR-0018). Boxwright auto-downloads the
    /// virtio-win driver ISO (or uses <see cref="VirtioWinIsoPath"/> if supplied) and injects the drivers
    /// into the Autounattend. Off by default — the in-box SATA + e1000e path needs no download.
    /// </summary>
    [ObservableProperty]
    private bool _useVirtio;

    /// <summary>Optional bring-your-own virtio-win.iso path; when set, Boxwright uses it instead of downloading.</summary>
    [ObservableProperty]
    private string? _virtioWinIsoPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrorMessage))]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelDownloadCommand))]
    private bool _isDownloading;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string? _progressText;

    /// <summary>The firmware choices offered (BIOS is the simplest first-boot default).</summary>
    public IReadOnlyList<string> FirmwareOptions { get; } = ["bios", "uefi"];

    /// <summary>The guest-OS choices offered (selects the virtual GPU; Linux is the default).</summary>
    public IReadOnlyList<string> OsTypeOptions { get; } = ["linux", "windows"];

    /// <summary>True when the selected guest OS is Windows (reveals the Windows install panel).</summary>
    public bool IsWindows => string.Equals(OsType, "windows", StringComparison.OrdinalIgnoreCase);

    /// <summary>True once a Windows installer ISO has been chosen.</summary>
    public bool HasWindowsIso => !string.IsNullOrWhiteSpace(WindowsIsoPath);

    // Whether the create flow should run the Windows unattended path (vs a plain blank-disk VM).
    private bool UnattendedWindowsActive => IsWindows && WindowsUnattended;

    /// <summary>The first validation problem, or null when the form is valid.</summary>
    public string? ValidationError
    {
        get
        {
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

            if (UnattendedWindowsActive)
            {
                if (!HasWindowsIso)
                {
                    return "Choose a Windows installer ISO.";
                }

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

    /// <summary>Raised with the created VM once the folder, config, and disk all exist.</summary>
    public event EventHandler<Vm>? Created;

    /// <summary>Raised when the user dismisses the form without creating.</summary>
    public event EventHandler? Cancelled;

    [RelayCommand]
    private async Task PickWindowsIsoAsync()
    {
        string? iso = await _filePicker.PickIsoAsync();
        if (!string.IsNullOrWhiteSpace(iso))
        {
            WindowsIsoPath = iso;
        }
    }

    [RelayCommand]
    private async Task PickVirtioWinIsoAsync()
    {
        string? iso = await _filePicker.PickIsoAsync();
        if (!string.IsNullOrWhiteSpace(iso))
        {
            VirtioWinIsoPath = iso;
        }
    }

    [RelayCommand(CanExecute = nameof(IsDownloading))]
    private void CancelDownload() => _cts?.Cancel();

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAsync()
    {
        IsBusy = true;
        ErrorMessage = null;

        if (UnattendedWindowsActive)
        {
            await CreateWindowsUnattendedAsync();
            return;
        }

        Vm vm;
        try
        {
            vm = await _repository.CreateAsync(BuildConfig());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = $"Couldn't create the VM: {ex.Message}";
            IsBusy = false;
            return;
        }

        try
        {
            string diskPath = Path.Combine(vm.FolderPath, DiskFileName);
            await _diskService.CreateAsync(diskPath, DiskSizeGiB * BytesPerGiB);
        }
        catch (DiskException ex)
        {
            // Roll back so the list never shows a VM without its disk.
            await _repository.DeleteAsync(vm.Config.Id);
            ErrorMessage = $"Couldn't create the disk: {ex.Message}";
            IsBusy = false;
            return;
        }

        IsBusy = false;
        Created?.Invoke(this, vm);
    }

    // Windows unattended: optionally fetch the virtio-win driver ISO (perf path), create the VM (Windows ISO
    // attached, SATA/virtio, e1000e/virtio-net), bake the Autounattend.xml seed CD and attach it, then
    // create the disk. Rolls back on failure.
    private async Task CreateWindowsUnattendedAsync()
    {
        // Resolve the virtio-win drivers first (bring-your-own, or download) so a cancel/failure aborts
        // before any VM is created. Null when not using virtio.
        string? virtioWinIso = null;
        if (UseVirtio)
        {
            virtioWinIso = await ResolveVirtioWinIsoAsync();
            if (virtioWinIso is null)
            {
                IsBusy = false; // cancelled or failed (ErrorMessage already set on failure)
                return;
            }
        }

        Vm vm;
        try
        {
            vm = await _repository.CreateAsync(BuildWindowsUnattendedConfig(WindowsIsoPath!, virtioWinIso));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = $"Couldn't create the VM: {ex.Message}";
            IsBusy = false;
            return;
        }

        try
        {
            bool uefi = string.Equals(Firmware, "uefi", StringComparison.OrdinalIgnoreCase);
            _autounattendSeedGenerator.Generate(BuildAnswers(), BuildWindowsOptions(), uefi, vm.FolderPath);
            VmConfig withSeed = vm.Config with
            {
                RemovableMedia =
                [
                    .. vm.Config.RemovableMedia,
                    new RemovableMediaConfig { Type = "cdrom", File = AutounattendSeedGenerator.SeedFileName, Attached = true },
                ],
            };
            await _repository.SaveAsync(withSeed);
            vm = vm with { Config = withSeed };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _repository.DeleteAsync(vm.Config.Id);
            ErrorMessage = $"Couldn't create the autounattend seed: {ex.Message}";
            IsBusy = false;
            return;
        }

        try
        {
            string diskPath = Path.Combine(vm.FolderPath, DiskFileName);
            await _diskService.CreateAsync(diskPath, DiskSizeGiB * BytesPerGiB);
        }
        catch (DiskException ex)
        {
            await _repository.DeleteAsync(vm.Config.Id);
            ErrorMessage = $"Couldn't create the disk: {ex.Message}";
            IsBusy = false;
            return;
        }

        IsBusy = false;
        Created?.Invoke(this, vm);
    }

    private bool CanCreate() => !IsBusy && ValidationError is null;

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke(this, EventArgs.Empty);

    /// <summary>Disposes the in-flight download cancellation source, if any.</summary>
    public void Dispose() => _cts?.Dispose();

    private VmConfig BuildConfig() => new()
    {
        Name = Name.Trim(),
        MemoryMiB = MemoryMiB,
        Cpu = new CpuConfig { Sockets = 1, Cores = CpuCores, Threads = 1 },
        Firmware = Firmware,
        OsType = OsType,
        Disks = [new DiskConfig { File = DiskFileName, Format = "qcow2", Interface = "virtio" }],
    };

    // Windows install: in-box SATA disk + e1000e NIC by default, or the faster virtio-blk + virtio-net when
    // a virtio-win driver ISO is supplied (attached as an extra CD; ADR-0018). The Windows ISO is attached
    // and CD-first; WindowsInstallInProgress drives the boot-from-CD auto-keypress + graduate (ADR-0015).
    private VmConfig BuildWindowsUnattendedConfig(string windowsIsoPath, string? virtioWinIsoPath)
    {
        bool virtio = virtioWinIsoPath is not null;
        List<RemovableMediaConfig> media = [new() { Type = "cdrom", File = windowsIsoPath, Attached = true }];
        if (virtio)
        {
            media.Add(new RemovableMediaConfig { Type = "cdrom", File = virtioWinIsoPath, Attached = true });
        }

        return new()
        {
            Name = Name.Trim(),
            MemoryMiB = MemoryMiB,
            Cpu = new CpuConfig { Sockets = 1, Cores = CpuCores, Threads = 1 },
            Firmware = Firmware,
            OsType = "windows",
            Disks = [new DiskConfig { File = DiskFileName, Format = "qcow2", Interface = virtio ? "virtio" : "sata" }],
            RemovableMedia = media,
            Network = new NetworkConfig { Model = virtio ? "virtio-net" : "e1000e" },
            Boot = new BootConfig { Order = "cd" },
            WindowsInstallInProgress = true,
        };
    }

    private UnattendedAnswers BuildAnswers() => new()
    {
        Hostname = Hostname.Trim(),
        Username = UnattendedUsername.Trim(),
        Password = UnattendedPassword,
    };

    private WindowsInstallOptions BuildWindowsOptions() => new()
    {
        ProductKey = string.IsNullOrWhiteSpace(ProductKey) ? null : ProductKey.Trim(),
        UseVirtio = UseVirtio,
    };

    // Bring-your-own virtio-win.iso, or download + cache the pinned one. Returns its path, or null on
    // cancel/failure (ErrorMessage set on failure). Shows download progress reused from the catalog flow.
    private async Task<string?> ResolveVirtioWinIsoAsync()
    {
        if (!string.IsNullOrWhiteSpace(VirtioWinIsoPath))
        {
            return VirtioWinIsoPath;
        }

        IsDownloading = true;
        ProgressPercent = 0;
        ProgressText = null;
        _cts = new CancellationTokenSource();
        try
        {
            var progress = new DispatchedProgress(_dispatcher, OnProgress);
            // Re-verify the cached virtio-win ISO before an install relies on it; a rotted copy would
            // otherwise fail the Windows install obscurely. One-time re-hash/re-download at use time.
            return await _downloader.EnsureAsync(VirtioWin.CatalogEntry, progress, reverifyCachedContent: true, cancellationToken: _cts.Token);
        }
        catch (OperationCanceledException)
        {
            return null; // deliberate cancel — no message
        }
        catch (DownloadException ex)
        {
            ErrorMessage = $"Couldn't download the virtio-win drivers: {ex.Message}";
            return null;
        }
        finally
        {
            IsDownloading = false;
            ProgressText = null;
            ProgressPercent = 0;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void OnProgress(IsoDownloadProgress progress)
    {
        ProgressPercent = progress.Percent ?? 0;
        ProgressText = progress.TotalBytes is > 0
            ? $"{Humanize(progress.BytesReceived)} / {Humanize(progress.TotalBytes.Value)}"
            : Humanize(progress.BytesReceived);
    }

    private static string Humanize(long bytes)
    {
        const double gb = 1_000_000_000d;
        const double mb = 1_000_000d;
        return bytes >= gb ? $"{bytes / gb:0.0} GB" : $"{bytes / mb:0} MB";
    }
}

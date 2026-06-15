using System.Collections.ObjectModel;
using Boxwright.App.Services;
using Boxwright.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Boxwright.App.ViewModels;

/// <summary>
/// The "Get an OS" gallery: lists catalog entries, prefills recommended specs for the
/// selected one (name editable), then hands the choice to <see cref="ICatalogVmInstaller"/>,
/// which downloads + verifies the image, prepares the disk, and writes any unattended/cloud-init
/// seed — the same Core orchestration the headless CLI uses (ADR-0022). This view model owns only
/// presentation: field prefill, validation, progress, and cancellation. UI-free, so it is unit-testable.
/// </summary>
public sealed partial class CatalogViewModel : ObservableObject, IDisposable
{
    private readonly IOsCatalogSource _catalogSource;
    private readonly ICatalogVmInstaller _installer;
    private readonly IUiDispatcher _dispatcher;
    private readonly Func<string, bool> _isNameTaken;
    private CancellationTokenSource? _cts;

    public CatalogViewModel(
        IOsCatalogSource catalogSource,
        ICatalogVmInstaller installer,
        IUiDispatcher dispatcher,
        Func<string, bool> isNameTaken)
    {
        ArgumentNullException.ThrowIfNull(catalogSource);
        ArgumentNullException.ThrowIfNull(installer);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(isNameTaken);

        _catalogSource = catalogSource;
        _installer = installer;
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

        try
        {
            // Core runs the whole sequence: download + verify (with progress/cancel), prepare the disk,
            // and write any unattended/cloud-init seed, rolling back the VM folder on failure (ADR-0022).
            var progress = new DispatchedProgress(_dispatcher, OnProgress);
            Vm vm = await _installer.CreateAsync(entry, BuildOptions(), progress, _cts.Token);
            ResetDownloadState();
            Created?.Invoke(this, vm);
        }
        catch (OperationCanceledException)
        {
            ResetDownloadState(); // deliberate cancel — no message, no VM
        }
        catch (Exception ex) when (ex is DownloadException or DiskException or InstallMediaException or IOException or UnauthorizedAccessException)
        {
            ErrorMessage = ex.Message;
            ResetDownloadState();
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
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

    // Translate the confirm-panel fields into the Core install request. A cloud image always carries a
    // seed (its login lives only there), so Core ignores the Unattended flag for it; an installer ISO
    // only gets a seed when the user opted in. Answers are supplied whenever a seed will be written.
    private CatalogInstallOptions BuildOptions() => new()
    {
        Name = Name.Trim(),
        MemoryMiB = MemoryMiB,
        CpuCores = CpuCores,
        DiskSizeGiB = DiskSizeGiB,
        Firmware = Firmware,
        Unattended = UnattendedActive && !IsCloudImage,
        Answers = UnattendedActive
            ? new UnattendedAnswers { Hostname = Hostname.Trim(), Username = UnattendedUsername.Trim(), Password = UnattendedPassword }
            : null,
    };

    private static string Humanize(long bytes)
    {
        const double gb = 1_000_000_000d;
        const double mb = 1_000_000d;
        return bytes >= gb ? $"{bytes / gb:0.0} GB" : $"{bytes / mb:0} MB";
    }
}

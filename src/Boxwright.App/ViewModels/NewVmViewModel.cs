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
public sealed partial class NewVmViewModel : ObservableObject
{
    /// <summary>The disk image file name created inside each new VM's folder.</summary>
    public const string DiskFileName = "disk.qcow2";

    private const long BytesPerGiB = 1024L * 1024 * 1024;

    private readonly VmRepository _repository;
    private readonly IDiskService _diskService;
    private readonly IFilePicker _filePicker;
    private readonly IAutounattendSeedGenerator _autounattendSeedGenerator;
    private readonly Func<string, bool> _isNameTaken;

    public NewVmViewModel(
        VmRepository repository,
        IDiskService diskService,
        IFilePicker filePicker,
        IAutounattendSeedGenerator autounattendSeedGenerator,
        Func<string, bool> isNameTaken)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(diskService);
        ArgumentNullException.ThrowIfNull(filePicker);
        ArgumentNullException.ThrowIfNull(autounattendSeedGenerator);
        ArgumentNullException.ThrowIfNull(isNameTaken);

        _repository = repository;
        _diskService = diskService;
        _filePicker = filePicker;
        _autounattendSeedGenerator = autounattendSeedGenerator;
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrorMessage))]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private bool _isBusy;

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

    // Windows unattended: create the VM (Windows ISO attached, SATA, e1000e), bake the Autounattend.xml
    // seed CD into the folder and attach it as a second CD, then create the disk. Rolls back on failure.
    private async Task CreateWindowsUnattendedAsync()
    {
        Vm vm;
        try
        {
            vm = await _repository.CreateAsync(BuildWindowsUnattendedConfig(WindowsIsoPath!));
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

    private VmConfig BuildConfig() => new()
    {
        Name = Name.Trim(),
        MemoryMiB = MemoryMiB,
        Cpu = new CpuConfig { Sockets = 1, Cores = CpuCores, Threads = 1 },
        Firmware = Firmware,
        OsType = OsType,
        Disks = [new DiskConfig { File = DiskFileName, Format = "qcow2", Interface = "virtio" }],
    };

    // Windows install: SATA disk (in-box driver), e1000e NIC, the Windows ISO attached, and CD-first boot.
    // WindowsInstallInProgress drives the boot-from-CD auto-keypress and the post-install graduate (ADR-0015).
    private VmConfig BuildWindowsUnattendedConfig(string windowsIsoPath) => new()
    {
        Name = Name.Trim(),
        MemoryMiB = MemoryMiB,
        Cpu = new CpuConfig { Sockets = 1, Cores = CpuCores, Threads = 1 },
        Firmware = Firmware,
        OsType = "windows",
        Disks = [new DiskConfig { File = DiskFileName, Format = "qcow2", Interface = "sata" }],
        RemovableMedia = [new RemovableMediaConfig { Type = "cdrom", File = windowsIsoPath, Attached = true }],
        Network = new NetworkConfig { Model = "e1000e" },
        Boot = new BootConfig { Order = "cd" },
        WindowsInstallInProgress = true,
    };

    private UnattendedAnswers BuildAnswers() => new()
    {
        Hostname = Hostname.Trim(),
        Username = UnattendedUsername.Trim(),
        Password = UnattendedPassword,
    };

    private WindowsInstallOptions BuildWindowsOptions() => new()
    {
        ProductKey = string.IsNullOrWhiteSpace(ProductKey) ? null : ProductKey.Trim(),
    };
}

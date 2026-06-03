using Boxwright.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Boxwright.App.ViewModels;

/// <summary>
/// The guided "New VM" form: collects name, memory, cores, disk size, and firmware
/// (with sane, overridable defaults), validates them, and on create writes the VM
/// folder + config (<see cref="VmRepository"/>) and a qcow2 disk (<see cref="IDiskService"/>).
/// A disk failure rolls back the half-created VM. UI-free, so it is unit-testable.
/// </summary>
public sealed partial class NewVmViewModel : ObservableObject
{
    /// <summary>The disk image file name created inside each new VM's folder.</summary>
    public const string DiskFileName = "disk.qcow2";

    private const long BytesPerGiB = 1024L * 1024 * 1024;

    private readonly VmRepository _repository;
    private readonly IDiskService _diskService;
    private readonly Func<string, bool> _isNameTaken;

    public NewVmViewModel(VmRepository repository, IDiskService diskService, Func<string, bool> isNameTaken)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(diskService);
        ArgumentNullException.ThrowIfNull(isNameTaken);

        _repository = repository;
        _diskService = diskService;
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
    private string _osType = "linux";

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

            return null;
        }
    }

    public bool HasValidationError => ValidationError is not null;

    public bool HasErrorMessage => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>Raised with the created VM once the folder, config, and disk all exist.</summary>
    public event EventHandler<Vm>? Created;

    /// <summary>Raised when the user dismisses the form without creating.</summary>
    public event EventHandler? Cancelled;

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAsync()
    {
        IsBusy = true;
        ErrorMessage = null;

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
}

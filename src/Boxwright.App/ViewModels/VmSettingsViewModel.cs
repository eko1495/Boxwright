using Boxwright.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Boxwright.App.ViewModels;

/// <summary>
/// Edits an existing VM's boot-time settings (name, memory, CPU cores, firmware,
/// display, boot menu) and persists them to the VM's <see cref="VmConfig"/>. Every
/// field here is a boot-time setting — it takes effect only on the next launch — so
/// saving rewrites the on-disk config and never touches a running process. All other
/// config (id, disks, removable media, networking) is preserved; disks are read-only.
/// </summary>
public sealed partial class VmSettingsViewModel : ObservableObject
{
    private readonly VmRepository _repository;
    private readonly VmConfig _original;
    private readonly Func<string, bool> _isNameTakenByOther;

    public VmSettingsViewModel(Vm vm, VmRepository repository, Func<string, bool> isNameTakenByOther, bool isRunning)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(isNameTakenByOther);

        _repository = repository;
        _original = vm.Config;
        _isNameTakenByOther = isNameTakenByOther;
        IsRunning = isRunning;

        _name = _original.Name;
        _memoryMiB = _original.MemoryMiB;
        _cpuCores = _original.Cpu.Cores;
        _firmware = _original.Firmware;
        _osType = _original.OsType;
        _displayProtocol = _original.Display.Protocol;
        _displayGl = _original.Display.Gl;
        _bootMenu = _original.Boot.Menu;
        _audioEnabled = _original.Audio.Enabled;

        DisksText = _original.Disks.Count == 0
            ? "(no disks)"
            : string.Join(Environment.NewLine, _original.Disks.Select(d => $"{d.File} — {d.Format}, {d.Interface}"));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError), nameof(HasValidationError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError), nameof(HasValidationError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private int _memoryMiB;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError), nameof(HasValidationError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private int _cpuCores;

    [ObservableProperty]
    private string _firmware;

    [ObservableProperty]
    private string _osType;

    [ObservableProperty]
    private string _displayProtocol;

    [ObservableProperty]
    private bool _displayGl;

    [ObservableProperty]
    private bool _bootMenu;

    [ObservableProperty]
    private bool _audioEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrorMessage))]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isBusy;

    /// <summary>True if the VM is currently running (changes won't apply until it is relaunched).</summary>
    public bool IsRunning { get; }

    public IReadOnlyList<string> FirmwareOptions { get; } = ["bios", "uefi"];

    public IReadOnlyList<string> OsTypeOptions { get; } = ["linux", "windows"];

    public IReadOnlyList<string> DisplayProtocolOptions { get; } = ["spice", "vnc"];

    /// <summary>Read-only summary of the VM's disks (disk editing is not part of this panel).</summary>
    public string DisksText { get; }

    public string? ValidationError
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                return "Enter a name for the VM.";
            }

            if (_isNameTakenByOther(Name.Trim()))
            {
                return $"Another VM named “{Name.Trim()}” already exists.";
            }

            if (MemoryMiB <= 0)
            {
                return "Memory must be greater than 0 MiB.";
            }

            if (CpuCores <= 0)
            {
                return "CPU cores must be greater than 0.";
            }

            return null;
        }
    }

    public bool HasValidationError => ValidationError is not null;

    public bool HasErrorMessage => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>Raised with the updated config once it has been persisted.</summary>
    public event EventHandler<VmConfig>? Saved;

    /// <summary>Raised when the user dismisses the panel without saving.</summary>
    public event EventHandler? Cancelled;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        IsBusy = true;
        ErrorMessage = null;

        // Preserve everything not exposed here (id, disks, removable media, networking, …).
        VmConfig updated = _original with
        {
            Name = Name.Trim(),
            MemoryMiB = MemoryMiB,
            Cpu = _original.Cpu with { Cores = CpuCores },
            Firmware = Firmware,
            OsType = OsType,
            Display = _original.Display with { Gl = DisplayGl, Protocol = DisplayProtocol },
            Audio = _original.Audio with { Enabled = AudioEnabled },
            Boot = _original.Boot with { Menu = BootMenu },
        };

        try
        {
            await _repository.SaveAsync(updated);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = $"Couldn't save settings: {ex.Message}";
            IsBusy = false;
            return;
        }

        IsBusy = false;
        Saved?.Invoke(this, updated);
    }

    private bool CanSave() => !IsBusy && ValidationError is null;

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke(this, EventArgs.Empty);
}

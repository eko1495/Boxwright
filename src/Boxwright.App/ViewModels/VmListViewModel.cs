using System.Collections.ObjectModel;
using Boxwright.App.Services;
using Boxwright.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Boxwright.App.ViewModels;

/// <summary>
/// Lists the VMs under the repository root and tracks the current selection. A refresh
/// reconciles the list with disk while preserving existing item view models (and their
/// live sessions), so reloading never drops a running VM's state.
/// </summary>
public sealed partial class VmListViewModel : ObservableObject
{
    private readonly VmRepository _repository;
    private readonly IVmLauncher _launcher;
    private readonly IUiDispatcher _dispatcher;
    private readonly IFilePicker _filePicker;
    private readonly IDisplayLauncher _displayLauncher;
    private readonly ILogReader _logReader;

    public VmListViewModel(
        VmRepository repository,
        IVmLauncher launcher,
        IUiDispatcher dispatcher,
        IFilePicker filePicker,
        IDisplayLauncher displayLauncher,
        ILogReader logReader)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(filePicker);
        ArgumentNullException.ThrowIfNull(displayLauncher);
        ArgumentNullException.ThrowIfNull(logReader);

        _repository = repository;
        _launcher = launcher;
        _dispatcher = dispatcher;
        _filePicker = filePicker;
        _displayLauncher = displayLauncher;
        _logReader = logReader;
    }

    /// <summary>The loaded VMs, sorted by name.</summary>
    public ObservableCollection<VmListItemViewModel> Vms { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private VmListItemViewModel? _selectedVm;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    /// <summary>True once a load has completed and found no VMs (drives the empty state).</summary>
    public bool IsEmpty => !IsLoading && Vms.Count == 0;

    /// <summary>True when the last load failed.</summary>
    public bool HasError => ErrorMessage is not null;

    /// <summary>True when a VM is selected (drives the detail/actions panel).</summary>
    public bool HasSelection => SelectedVm is not null;

    /// <summary>The VMs with a live QEMU session (running/paused) — drives the app-close prompt.</summary>
    public IReadOnlyList<VmListItemViewModel> RunningVms => Vms.Where(v => v.IsLive).ToList();

    /// <summary>True when any VM still has a live session (e.g. to confirm before the app closes).</summary>
    public bool HasRunningVms => Vms.Any(v => v.IsLive);

    /// <summary>Gracefully shuts down every live VM (ACPI power-down, force after the grace period).</summary>
    public Task ShutDownAllAsync() => Task.WhenAll(RunningVms.Select(v => v.ShutDownAsync()));

    /// <summary>Immediately force-stops every live VM (pulls the plug on each).</summary>
    public Task ForceOffAllAsync() => Task.WhenAll(RunningVms.Select(v => v.ForceOffAsync()));

    /// <summary>Inserts a freshly created VM into the list (kept sorted) and selects it.</summary>
    public void AddCreated(Vm vm)
    {
        ArgumentNullException.ThrowIfNull(vm);

        VmListItemViewModel item = CreateItem(vm);

        int index = 0;
        while (index < Vms.Count &&
               string.Compare(Vms[index].Name, item.Name, StringComparison.OrdinalIgnoreCase) < 0)
        {
            index++;
        }

        Vms.Insert(index, item);
        SelectedVm = item;
        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            IReadOnlyList<Vm> loaded = await _repository.ListAsync(cancellationToken);
            MergeInto(loaded);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = $"Could not read the VMs folder: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private VmListItemViewModel CreateItem(Vm vm)
    {
        var item = new VmListItemViewModel(vm, _launcher, _repository, _dispatcher, _filePicker, _displayLauncher, _logReader);
        item.Deleted += OnItemDeleted;
        return item;
    }

    private void MergeInto(IReadOnlyList<Vm> loaded)
    {
        string? selectedId = SelectedVm?.Vm.Config.Id;

        var existing = new Dictionary<string, VmListItemViewModel>(StringComparer.Ordinal);
        foreach (VmListItemViewModel item in Vms)
        {
            existing[item.Vm.Config.Id] = item;
        }

        var ordered = new List<VmListItemViewModel>();
        foreach (Vm vm in loaded.OrderBy(v => v.Config.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (existing.Remove(vm.Config.Id, out VmListItemViewModel? keep))
            {
                ordered.Add(keep); // Preserve the live item (and its running session).
            }
            else
            {
                ordered.Add(CreateItem(vm));
            }
        }

        // Whatever is still in `existing` vanished from disk — detach it.
        foreach (VmListItemViewModel removed in existing.Values)
        {
            removed.Deleted -= OnItemDeleted;
        }

        Vms.Clear();
        foreach (VmListItemViewModel item in ordered)
        {
            Vms.Add(item);
        }

        SelectedVm = selectedId is null
            ? null
            : Vms.FirstOrDefault(i => string.Equals(i.Vm.Config.Id, selectedId, StringComparison.Ordinal));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void OnItemDeleted(object? sender, EventArgs e)
    {
        if (sender is not VmListItemViewModel item)
        {
            return;
        }

        item.Deleted -= OnItemDeleted;
        if (ReferenceEquals(SelectedVm, item))
        {
            SelectedVm = null;
        }

        Vms.Remove(item);
        OnPropertyChanged(nameof(IsEmpty));
    }
}

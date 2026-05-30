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

    public VmListViewModel(VmRepository repository, IVmLauncher launcher, IUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(dispatcher);

        _repository = repository;
        _launcher = launcher;
        _dispatcher = dispatcher;
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
                var item = new VmListItemViewModel(vm, _launcher, _repository, _dispatcher);
                item.Deleted += OnItemDeleted;
                ordered.Add(item);
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

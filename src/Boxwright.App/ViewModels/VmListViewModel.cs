using System.Collections.ObjectModel;
using Boxwright.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Boxwright.App.ViewModels;

/// <summary>
/// Lists the VMs found under the repository root and tracks the current selection.
/// Loading is exposed as <see cref="RefreshCommand"/> so the view can trigger it on
/// open and from a Refresh button; the work is unit-testable without any UI.
/// </summary>
public sealed partial class VmListViewModel : ObservableObject
{
    private readonly VmRepository _repository;

    public VmListViewModel(VmRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    /// <summary>The loaded VMs, sorted by name.</summary>
    public ObservableCollection<VmListItemViewModel> Vms { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLoading;

    [ObservableProperty]
    private VmListItemViewModel? _selectedVm;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    /// <summary>True once a load has completed and found no VMs (drives the empty state).</summary>
    public bool IsEmpty => !IsLoading && Vms.Count == 0;

    /// <summary>True when the last load failed.</summary>
    public bool HasError => ErrorMessage is not null;

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            IReadOnlyList<Vm> loaded = await _repository.ListAsync(cancellationToken);

            Vms.Clear();
            foreach (Vm vm in loaded.OrderBy(v => v.Config.Name, StringComparer.OrdinalIgnoreCase))
            {
                Vms.Add(new VmListItemViewModel(vm));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = $"Could not read the VMs folder: {ex.Message}";
        }
        finally
        {
            // Resetting IsLoading also re-raises IsEmpty (NotifyPropertyChangedFor),
            // which now reflects the freshly populated collection.
            IsLoading = false;
        }
    }
}

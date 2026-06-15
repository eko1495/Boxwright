using System.Collections.ObjectModel;
using Boxwright.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Boxwright.App.ViewModels;

/// <summary>
/// Edits a VM's USB passthrough list (ADR-0023) inside the settings panel: pick from the host's USB
/// devices (where the OS can enumerate them) or add one by vendor:product, and remove configured ones.
/// Owns no persistence — the parent <see cref="VmSettingsViewModel"/> reads <see cref="Devices"/> when
/// it saves. UI-free, so it is unit-testable.
/// </summary>
public sealed partial class UsbDevicesViewModel : ObservableObject
{
    private readonly IUsbDeviceEnumerator _enumerator;

    public UsbDevicesViewModel(IUsbDeviceEnumerator enumerator, IReadOnlyList<UsbPassthroughConfig> initial)
    {
        ArgumentNullException.ThrowIfNull(enumerator);
        ArgumentNullException.ThrowIfNull(initial);
        _enumerator = enumerator;
        foreach (UsbPassthroughConfig device in initial)
        {
            Devices.Add(device);
        }
    }

    /// <summary>The VM's configured passthrough devices (what the parent persists).</summary>
    public ObservableCollection<UsbPassthroughConfig> Devices { get; } = [];

    /// <summary>Host devices discovered by <see cref="LoadHostDevicesCommand"/> (empty until loaded / when unsupported).</summary>
    public ObservableCollection<HostUsbDevice> HostDevices { get; } = [];

    /// <summary>True when this host can enumerate USB devices (the picker list is meaningful).</summary>
    public bool CanListHost => _enumerator.IsSupported;

    /// <summary>True when host enumeration is unsupported (show the "add by vendor:product" hint).</summary>
    public bool HostListingUnsupported => !_enumerator.IsSupported;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddSelectedCommand))]
    private HostUsbDevice? _selectedHostDevice;

    [ObservableProperty]
    private string _manualEntry = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrorMessage))]
    private string? _errorMessage;

    public bool HasErrorMessage => !string.IsNullOrEmpty(ErrorMessage);

    [RelayCommand]
    private async Task LoadHostDevicesAsync()
    {
        ErrorMessage = null;
        if (!_enumerator.IsSupported)
        {
            return;
        }

        try
        {
            IReadOnlyList<HostUsbDevice> found = await _enumerator.ListAsync();
            HostDevices.Clear();
            foreach (HostUsbDevice device in found)
            {
                HostDevices.Add(device);
            }
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException)
        {
            ErrorMessage = $"Couldn't list host USB devices: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanAddSelected))]
    private void AddSelected()
    {
        if (SelectedHostDevice is { } device)
        {
            TryAdd(device.VendorId, device.ProductId, device.Description);
        }
    }

    private bool CanAddSelected() => SelectedHostDevice is not null;

    [RelayCommand]
    private void AddManual()
    {
        string[] parts = (ManualEntry ?? string.Empty).Split(':');
        if (parts.Length != 2 || !UsbId.IsValid(parts[0].ToLowerInvariant()) || !UsbId.IsValid(parts[1].ToLowerInvariant()))
        {
            ErrorMessage = "Enter a USB id as vendor:product, four hex digits each (e.g. 046d:c52b).";
            return;
        }

        if (TryAdd(parts[0].ToLowerInvariant(), parts[1].ToLowerInvariant(), description: null))
        {
            ManualEntry = string.Empty;
        }
    }

    [RelayCommand]
    private void Remove(UsbPassthroughConfig? device)
    {
        if (device is not null)
        {
            Devices.Remove(device);
        }
    }

    private bool TryAdd(string vendor, string product, string? description)
    {
        if (Devices.Any(d =>
                string.Equals(d.VendorId, vendor, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(d.ProductId, product, StringComparison.OrdinalIgnoreCase)))
        {
            ErrorMessage = $"{vendor}:{product} is already on the list.";
            return false;
        }

        Devices.Add(new UsbPassthroughConfig
        {
            VendorId = vendor,
            ProductId = product,
            Description = string.IsNullOrWhiteSpace(description) ? null : description,
        });
        ErrorMessage = null;
        return true;
    }
}

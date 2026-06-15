namespace Boxwright.Core;

/// <summary>A USB device connected to the host, identified by vendor:product (ADR-0023).</summary>
/// <param name="VendorId">USB vendor id, four lowercase hex digits (e.g. <c>046d</c>).</param>
/// <param name="ProductId">USB product id, four lowercase hex digits (e.g. <c>c52b</c>).</param>
/// <param name="Description">A human label (manufacturer + product), or empty when the host reports none.</param>
public readonly record struct HostUsbDevice(string VendorId, string ProductId, string Description);

/// <summary>
/// Lists the host's connected USB devices so the user can pick one to pass through. Capability-gated
/// per OS (Directive 4): host USB enumeration has no portable API, so a host that can't enumerate
/// reports <see cref="IsSupported"/> = <see langword="false"/> and the user adds a device by
/// vendor:product manually. The passthrough wiring itself (see <see cref="CommandLineBuilder"/>) is
/// OS-agnostic. Implemented by <see cref="LinuxUsbDeviceEnumerator"/> / <see cref="UnsupportedUsbDeviceEnumerator"/>.
/// </summary>
public interface IUsbDeviceEnumerator
{
    /// <summary>True when this host can enumerate USB devices (the device picker is available).</summary>
    bool IsSupported { get; }

    /// <summary>Lists the connected host USB devices.</summary>
    /// <exception cref="NotSupportedException">Enumeration isn't supported on this host (<see cref="IsSupported"/> is false).</exception>
    Task<IReadOnlyList<HostUsbDevice>> ListAsync(CancellationToken cancellationToken = default);
}

/// <summary>Picks the host-appropriate <see cref="IUsbDeviceEnumerator"/>.</summary>
public static class UsbDeviceEnumerator
{
    /// <summary>Returns the Linux sysfs enumerator on Linux, otherwise the capability-gated unsupported one.</summary>
    public static IUsbDeviceEnumerator CreateDefault() =>
        OperatingSystem.IsLinux() ? new LinuxUsbDeviceEnumerator() : new UnsupportedUsbDeviceEnumerator();
}

/// <summary>The enumerator for hosts where listing isn't implemented yet (Windows/macOS): reports unsupported.</summary>
public sealed class UnsupportedUsbDeviceEnumerator : IUsbDeviceEnumerator
{
    /// <inheritdoc />
    public bool IsSupported => false;

    /// <inheritdoc />
    public Task<IReadOnlyList<HostUsbDevice>> ListAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "Listing host USB devices isn't supported on this operating system yet; add a device by its vendor:product id instead.");
}

/// <summary>
/// Enumerates host USB devices from Linux <c>sysfs</c> (<c>/sys/bus/usb/devices</c>) — no external tool,
/// no dependency. Each device directory exposes <c>idVendor</c>/<c>idProduct</c> (four hex digits) and,
/// optionally, <c>manufacturer</c>/<c>product</c> text. Interface sub-nodes and the Linux Foundation
/// root hubs are skipped. The sysfs root is injectable for testing.
/// </summary>
public sealed class LinuxUsbDeviceEnumerator : IUsbDeviceEnumerator
{
    private const string DefaultSysfsRoot = "/sys/bus/usb/devices";
    private const string RootHubVendorId = "1d6b"; // Linux Foundation root hubs — not real passthrough targets

    private readonly string _sysfsRoot;

    /// <summary>Creates the enumerator, optionally over a non-default sysfs root (for tests).</summary>
    public LinuxUsbDeviceEnumerator(string? sysfsRoot = null) => _sysfsRoot = sysfsRoot ?? DefaultSysfsRoot;

    /// <inheritdoc />
    public bool IsSupported => OperatingSystem.IsLinux() && Directory.Exists(_sysfsRoot);

    /// <inheritdoc />
    public async Task<IReadOnlyList<HostUsbDevice>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_sysfsRoot))
        {
            return [];
        }

        var devices = new List<HostUsbDevice>();
        foreach (string dir in Directory.EnumerateDirectories(_sysfsRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? vendor = await ReadHexIdAsync(dir, "idVendor", cancellationToken);
            string? product = await ReadHexIdAsync(dir, "idProduct", cancellationToken);
            if (vendor is null || product is null || string.Equals(vendor, RootHubVendorId, StringComparison.OrdinalIgnoreCase))
            {
                continue; // an interface node (no ids) or a root hub
            }

            devices.Add(new HostUsbDevice(vendor, product, await ReadDescriptionAsync(dir, cancellationToken)));
        }

        return devices
            .OrderBy(d => d.VendorId, StringComparer.Ordinal)
            .ThenBy(d => d.ProductId, StringComparer.Ordinal)
            .ToList();
    }

    private static async Task<string?> ReadHexIdAsync(string dir, string fileName, CancellationToken cancellationToken)
    {
        string path = Path.Combine(dir, fileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            string value = (await File.ReadAllTextAsync(path, cancellationToken)).Trim().ToLowerInvariant();
            return UsbId.IsValid(value) ? value : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static async Task<string> ReadDescriptionAsync(string dir, CancellationToken cancellationToken)
    {
        string manufacturer = await ReadTextAsync(dir, "manufacturer", cancellationToken);
        string product = await ReadTextAsync(dir, "product", cancellationToken);
        return string.Join(' ', new[] { manufacturer, product }.Where(s => s.Length > 0)).Trim();
    }

    private static async Task<string> ReadTextAsync(string dir, string fileName, CancellationToken cancellationToken)
    {
        string path = Path.Combine(dir, fileName);
        if (!File.Exists(path))
        {
            return string.Empty;
        }

        try
        {
            return (await File.ReadAllTextAsync(path, cancellationToken)).Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }
}

/// <summary>Validation/parsing for a USB vendor or product id (four hex digits).</summary>
public static class UsbId
{
    /// <summary>True if <paramref name="value"/> is exactly four hexadecimal digits.</summary>
    public static bool IsValid(string? value) =>
        value is { Length: 4 } && value.All(Uri.IsHexDigit);

    /// <summary>
    /// The QEMU device id for a passed-through USB device, derived deterministically from its
    /// vendor:product (e.g. <c>usb-046d-c52b</c>). Shared by the boot-time command line
    /// (<see cref="CommandLineBuilder"/>) and live hot-plug/unplug (<see cref="RunningVm"/>), so the
    /// same device has one stable handle however it was attached.
    /// </summary>
    public static string DeviceId(string vendorId, string productId) =>
        $"usb-{vendorId.ToLowerInvariant()}-{productId.ToLowerInvariant()}";
}

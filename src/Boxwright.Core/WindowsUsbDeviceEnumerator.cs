using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Boxwright.Core;

/// <summary>
/// Parses a Windows device "hardware id" into a USB vendor:product. Hardware ids look like
/// <c>USB\VID_046D&amp;PID_C52B&amp;REV_2400</c> (case-insensitive). Pure and testable — the bug-prone
/// part of the Windows enumerator that does not need a Windows host to verify.
/// </summary>
public static class UsbHardwareId
{
    /// <summary>Extracts the vendor and product ids (four lowercase hex digits) from a hardware id, if present.</summary>
    public static bool TryParse(string? hardwareId, out string vendorId, out string productId)
    {
        vendorId = string.Empty;
        productId = string.Empty;
        if (string.IsNullOrEmpty(hardwareId))
        {
            return false;
        }

        string? vendor = ReadHexAfter(hardwareId, "VID_");
        string? product = ReadHexAfter(hardwareId, "PID_");
        if (vendor is null || product is null)
        {
            return false;
        }

        vendorId = vendor;
        productId = product;
        return true;
    }

    // The four hex digits following a marker (e.g. "VID_"), case-insensitive; null if absent/too short.
    private static string? ReadHexAfter(string value, string marker)
    {
        int at = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (at < 0)
        {
            return null;
        }

        int start = at + marker.Length;
        if (start + 4 > value.Length)
        {
            return null;
        }

        string candidate = value.Substring(start, 4);
        return candidate.All(Uri.IsHexDigit) ? candidate.ToLowerInvariant() : null;
    }
}

/// <summary>
/// Enumerates host USB devices on Windows via SetupAPI (no dependency, no QEMU link — pure P/Invoke into
/// the OS; ADR-0005), reading each device's hardware id and parsing vendor:product with
/// <see cref="UsbHardwareId"/>. Windows-gated; a no-op elsewhere.
/// </summary>
public sealed class WindowsUsbDeviceEnumerator : IUsbDeviceEnumerator
{
    private const int DigcfPresent = 0x2;
    private const int DigcfAllClasses = 0x4;
    private const int SpdrpDeviceDesc = 0x0;
    private const int SpdrpHardwareId = 0x1;
    private const int SpdrpFriendlyName = 0xC;
    private const int ErrorInsufficientBuffer = 122;

    /// <inheritdoc />
    public bool IsSupported => OperatingSystem.IsWindows();

    /// <inheritdoc />
    public Task<IReadOnlyList<HostUsbDevice>> ListAsync(CancellationToken cancellationToken = default) =>
        OperatingSystem.IsWindows()
            ? Task.FromResult<IReadOnlyList<HostUsbDevice>>(Enumerate())
            : Task.FromResult<IReadOnlyList<HostUsbDevice>>([]);

    [SupportedOSPlatform("windows")]
    private static List<HostUsbDevice> Enumerate()
    {
        var devices = new List<HostUsbDevice>();
        IntPtr set = SetupDiGetClassDevs(IntPtr.Zero, "USB", IntPtr.Zero, DigcfPresent | DigcfAllClasses);
        if (set == IntPtr.Zero || set == new IntPtr(-1))
        {
            return devices;
        }

        try
        {
            var data = new SpDevinfoData { cbSize = (uint)Marshal.SizeOf<SpDevinfoData>() };
            for (int index = 0; SetupDiEnumDeviceInfo(set, index, ref data); index++)
            {
                string? hardwareId = GetStringProperty(set, ref data, SpdrpHardwareId);
                if (!UsbHardwareId.TryParse(hardwareId, out string vendor, out string product))
                {
                    continue; // a USB hub/host controller or a node without VID/PID
                }

                string description =
                    GetStringProperty(set, ref data, SpdrpFriendlyName)
                    ?? GetStringProperty(set, ref data, SpdrpDeviceDesc)
                    ?? string.Empty;
                devices.Add(new HostUsbDevice(vendor, product, description.Trim()));
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }

        return devices
            .OrderBy(d => d.VendorId, StringComparer.Ordinal)
            .ThenBy(d => d.ProductId, StringComparer.Ordinal)
            .ToList();
    }

    // Reads a string device property (the first string of a REG_MULTI_SZ, e.g. the primary hardware id).
    [SupportedOSPlatform("windows")]
    private static string? GetStringProperty(IntPtr set, ref SpDevinfoData data, int property)
    {
        // First call sizes the buffer; the second fills it.
        SetupDiGetDeviceRegistryProperty(set, ref data, property, out _, null, 0, out int required);
        if (required <= 0 && Marshal.GetLastWin32Error() != ErrorInsufficientBuffer)
        {
            return null;
        }

        byte[] buffer = new byte[Math.Max(required, 2)];
        if (!SetupDiGetDeviceRegistryProperty(set, ref data, property, out _, buffer, buffer.Length, out _))
        {
            return null;
        }

        // REG_SZ / REG_MULTI_SZ are UTF-16; take the first null-terminated string.
        string raw = Encoding.Unicode.GetString(buffer);
        int end = raw.IndexOf('\0');
        return end >= 0 ? raw[..end] : raw;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevinfoData
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(IntPtr classGuid, string? enumerator, IntPtr hwndParent, int flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, int memberIndex, ref SpDevinfoData deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceRegistryProperty(
        IntPtr deviceInfoSet,
        ref SpDevinfoData deviceInfoData,
        int property,
        out int propertyRegDataType,
        byte[]? propertyBuffer,
        int propertyBufferSize,
        out int requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);
}

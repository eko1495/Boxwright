using System.Text.Json;

namespace Boxwright.Core;

/// <summary>
/// Enumerates host USB devices on macOS by running <c>system_profiler SPUSBDataType -json</c> (a built-in
/// tool — no dependency, no P/Invoke; ADR-0005) and parsing its JSON tree. The parse is a pure, testable
/// function (<see cref="ParseSystemProfilerJson"/>); only the process invocation is macOS-gated.
/// </summary>
public sealed class MacOsUsbDeviceEnumerator : IUsbDeviceEnumerator
{
    private const string SystemProfiler = "system_profiler";

    private readonly IProcessRunner _processRunner;

    /// <summary>Creates the enumerator over the given process runner (used to invoke <c>system_profiler</c>).</summary>
    public MacOsUsbDeviceEnumerator(IProcessRunner processRunner)
    {
        ArgumentNullException.ThrowIfNull(processRunner);
        _processRunner = processRunner;
    }

    /// <inheritdoc />
    public bool IsSupported => OperatingSystem.IsMacOS();

    /// <inheritdoc />
    public async Task<IReadOnlyList<HostUsbDevice>> ListAsync(CancellationToken cancellationToken = default)
    {
        ProcessResult result = await _processRunner.RunAsync(
            SystemProfiler, ["SPUSBDataType", "-json"], cancellationToken);
        if (result.ExitCode != 0)
        {
            return [];
        }

        return ParseSystemProfilerJson(result.StandardOutput);
    }

    /// <summary>
    /// Parses <c>system_profiler SPUSBDataType -json</c> output: a tree under <c>SPUSBDataType</c> whose
    /// nodes nest via <c>_items</c>. Any node carrying both <c>vendor_id</c> and <c>product_id</c> is a
    /// device; the ids look like <c>0x046d</c> (sometimes with a trailing <c>(Vendor Name)</c>).
    /// </summary>
    public static IReadOnlyList<HostUsbDevice> ParseSystemProfilerJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var devices = new List<HostUsbDevice>();
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("SPUSBDataType", out JsonElement root))
            {
                Walk(root, devices);
            }
        }
        catch (JsonException)
        {
            return [];
        }

        return devices;
    }

    private static void Walk(JsonElement node, List<HostUsbDevice> devices)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (JsonElement child in node.EnumerateArray())
                {
                    Walk(child, devices);
                }

                break;

            case JsonValueKind.Object:
                string? vendor = ExtractHexId(StringProp(node, "vendor_id"));
                string? product = ExtractHexId(StringProp(node, "product_id"));
                if (vendor is not null && product is not null)
                {
                    devices.Add(new HostUsbDevice(vendor, product, Describe(node)));
                }

                if (node.TryGetProperty("_items", out JsonElement items))
                {
                    Walk(items, devices);
                }

                break;
        }
    }

    private static string? StringProp(JsonElement node, string name) =>
        node.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Describe(JsonElement node)
    {
        string manufacturer = StringProp(node, "manufacturer") ?? string.Empty;
        string name = StringProp(node, "_name") ?? string.Empty;
        return string.Join(' ', new[] { manufacturer, name }.Where(s => s.Length > 0)).Trim();
    }

    // "0x046d  (Logitech Inc.)" or "0x046d" -> "046d"; null when no hex follows. Takes the last four
    // hex digits (USB ids are 16-bit), left-padding a short value.
    private static string? ExtractHexId(string? raw)
    {
        if (raw is null)
        {
            return null;
        }

        string s = raw.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            s = s[2..];
        }

        int n = 0;
        while (n < s.Length && Uri.IsHexDigit(s[n]))
        {
            n++;
        }

        if (n == 0)
        {
            return null;
        }

        string hex = s[..n].ToLowerInvariant();
        return hex.Length >= 4 ? hex[^4..] : hex.PadLeft(4, '0');
    }
}

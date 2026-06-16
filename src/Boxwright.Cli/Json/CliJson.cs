using System.Text.Json;
using System.Text.Json.Serialization;

namespace Boxwright.Cli.Json;

/// <summary>
/// Machine-readable shapes for <c>--json</c> output, plus a source-generated serializer context
/// (no reflection — trim/AOT clean, matching the repo's System.Text.Json convention). Keys are
/// camelCase for easy consumption by <c>jq</c> and friends.
/// </summary>
internal static class CliJson
{
    /// <summary>Serializes <paramref name="value"/> to indented camelCase JSON via the source-gen context.</summary>
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, typeof(T), CliJsonContext.Default);
}

/// <summary>A VM as listed by <c>list --json</c>. <c>diskActualBytes</c> is the on-disk footprint (null if unmeasurable).</summary>
internal sealed record VmSummaryJson(string Id, string Name, string Status, string OsType, string Arch, int MemoryMiB, long? DiskActualBytes);

/// <summary>A disk in <c>info --json</c>, with its actual/virtual size (null when it couldn't be measured).</summary>
internal sealed record DiskJson(string File, string Format, string Interface, long? ActualBytes, long? VirtualBytes);

/// <summary>A removable-media slot in <c>info --json</c>.</summary>
internal sealed record MediaJson(string Type, string? File, bool Attached);

/// <summary>A VM's full configuration as emitted by <c>info --json</c>.</summary>
internal sealed record VmInfoJson(
    string Id,
    string Name,
    string Status,
    string Folder,
    string OsType,
    string Arch,
    string Machine,
    string Firmware,
    string Accelerator,
    string CpuModel,
    int CpuSockets,
    int CpuCores,
    int CpuThreads,
    int MemoryMiB,
    string DisplayProtocol,
    bool DisplayGl,
    string NetworkMode,
    string NetworkModel,
    bool AudioEnabled,
    IReadOnlyList<DiskJson> Disks,
    IReadOnlyList<MediaJson> RemovableMedia,
    long? DiskActualBytes,
    long? DiskVirtualBytes);

/// <summary>An OS catalog entry as listed by <c>os list --json</c>.</summary>
internal sealed record OsEntryJson(string Id, string Name, string Version, string Arch, bool SupportsAutoinstall);

/// <summary>A snapshot as listed by <c>snapshot list --json</c>.</summary>
internal sealed record SnapshotJson(string Tag, bool HasVmState, string Created);

/// <summary>A USB device as listed by <c>usb list --json</c> / <c>usb show --json</c>.</summary>
internal sealed record UsbJson(string Id, string VendorId, string ProductId, string? Description);

/// <summary>A VM's networking as emitted by <c>net show --json</c>.</summary>
internal sealed record NetworkJson(string Mode, string Model, string MacAddress, string Bridge, string TapDevice);

/// <summary>One disk's integrity result in <c>check --json</c> (null counts when the check couldn't run — see <c>error</c>).</summary>
internal sealed record DiskCheckJson(string File, bool? Healthy, long? Corruptions, long? Leaks, string? Error);

/// <summary>A VM's integrity report as emitted by <c>check --json</c>.</summary>
internal sealed record IntegrityJson(bool Healthy, bool Checked, DiskCheckJson[] Disks);

/// <summary>A recipe file's parse result as emitted by <c>recipe list --json</c>.</summary>
internal sealed record RecipeJson(string File, bool Ok, string[] Entries, string? Error);

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(VmSummaryJson[]))]
[JsonSerializable(typeof(VmInfoJson))]
[JsonSerializable(typeof(OsEntryJson[]))]
[JsonSerializable(typeof(SnapshotJson[]))]
[JsonSerializable(typeof(UsbJson[]))]
[JsonSerializable(typeof(NetworkJson))]
[JsonSerializable(typeof(RecipeJson[]))]
[JsonSerializable(typeof(IntegrityJson))]
internal sealed partial class CliJsonContext : JsonSerializerContext;

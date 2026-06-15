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

/// <summary>A VM as listed by <c>list --json</c>.</summary>
internal sealed record VmSummaryJson(string Id, string Name, string Status, string OsType, string Arch, int MemoryMiB);

/// <summary>A disk in <c>info --json</c>.</summary>
internal sealed record DiskJson(string File, string Format, string Interface);

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
    IReadOnlyList<MediaJson> RemovableMedia);

/// <summary>An OS catalog entry as listed by <c>os list --json</c>.</summary>
internal sealed record OsEntryJson(string Id, string Name, string Version, string Arch, bool SupportsAutoinstall);

/// <summary>A snapshot as listed by <c>snapshot list --json</c>.</summary>
internal sealed record SnapshotJson(string Tag, bool HasVmState, string Created);

/// <summary>A USB device as listed by <c>usb list --json</c> / <c>usb show --json</c>.</summary>
internal sealed record UsbJson(string Id, string VendorId, string ProductId, string? Description);

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(VmSummaryJson[]))]
[JsonSerializable(typeof(VmInfoJson))]
[JsonSerializable(typeof(OsEntryJson[]))]
[JsonSerializable(typeof(SnapshotJson[]))]
[JsonSerializable(typeof(UsbJson[]))]
internal sealed partial class CliJsonContext : JsonSerializerContext;

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Boxwright.Core;

/// <summary>Reads/writes a VM's <see cref="VmRuntimeState"/> so a restarted app can re-adopt it.</summary>
public interface IVmRuntimeStore
{
    /// <summary>Persists the runtime state for <paramref name="vm"/> (overwriting any existing file).</summary>
    void Save(Vm vm, VmRuntimeState state);

    /// <summary>Loads the runtime state for <paramref name="vm"/>, or null if absent/unreadable/stale.</summary>
    VmRuntimeState? TryLoad(Vm vm);

    /// <summary>Removes the runtime state for <paramref name="vm"/> (best-effort).</summary>
    void Clear(Vm vm);
}

/// <summary>
/// Persists a VM's <see cref="VmRuntimeState"/> to <c>runtime.json</c> in its folder (beside
/// <c>vm.json</c>), so a restarted Boxwright can reconnect to the running QEMU (ADR-0014). Reads and
/// deletes are best-effort: a missing, unreadable, or stale-schema file simply means "not running".
/// </summary>
public sealed class VmRuntimeStore : IVmRuntimeStore
{
    /// <summary>The per-VM runtime-state file name.</summary>
    public const string FileName = "runtime.json";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <inheritdoc />
    public void Save(Vm vm, VmRuntimeState state)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(state);
        File.WriteAllText(PathFor(vm), JsonSerializer.Serialize(state, Options));
    }

    /// <inheritdoc />
    public VmRuntimeState? TryLoad(Vm vm)
    {
        ArgumentNullException.ThrowIfNull(vm);
        try
        {
            string path = PathFor(vm);
            if (!File.Exists(path))
            {
                return null;
            }

            VmRuntimeState? state = JsonSerializer.Deserialize<VmRuntimeState>(File.ReadAllText(path), Options);
            return state?.SchemaVersion == VmRuntimeState.CurrentSchemaVersion ? state : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public void Clear(Vm vm)
    {
        ArgumentNullException.ThrowIfNull(vm);
        try
        {
            File.Delete(PathFor(vm));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort: a leftover file is harmless (the next adopt attempt re-checks the PID).
        }
    }

    private static string PathFor(Vm vm) => Path.Combine(vm.FolderPath, FileName);
}

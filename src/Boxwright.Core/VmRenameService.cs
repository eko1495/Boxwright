using System.Globalization;
using System.Text;

namespace Boxwright.Core;

/// <summary>
/// The default <see cref="IVmRenameService"/>. Renaming changes the display name and re-slugs the folder,
/// but the VM <see cref="VmConfig.Id"/> remains the immutable key (ADR-0028): linked-clone backing chains
/// store an <em>absolute</em> path into the source folder, so the id — not the folder — is what nothing may
/// break. The move is guarded against the two ways it could corrupt data: a linked-clone dependent (reusing
/// <see cref="IVmDeletionService.FindDependentsAsync"/> rather than re-deriving the detection) and a running
/// VM (which holds open file handles).
/// </summary>
public sealed class VmRenameService : IVmRenameService
{
    // Windows is the strictest target (Directive 4): it forbids these characters in a path segment, plus a
    // trailing dot/space and a set of reserved device names. macOS/Linux only forbid '/' and NUL, so
    // sanitizing for Windows yields a name valid everywhere.
    private static readonly char[] InvalidNameChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    // Keep the readable part short enough that even on legacy 260-char-path systems the full VM folder path
    // (root + slug + nested disk/seed file names) stays comfortably under the limit.
    private const int MaxNameSlugLength = 48;

    private readonly VmRepository _repository;
    private readonly IVmDeletionService _deletion;
    private readonly IVmRuntimeStore _runtimeStore;

    /// <summary>Creates a rename service over the repository, the dependency guard, and the runtime store.</summary>
    public VmRenameService(VmRepository repository, IVmDeletionService deletion, IVmRuntimeStore runtimeStore)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(deletion);
        ArgumentNullException.ThrowIfNull(runtimeStore);
        _repository = repository;
        _deletion = deletion;
        _runtimeStore = runtimeStore;
    }

    private static StringComparison FolderComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    /// <inheritdoc />
    public async Task<Vm> RenameAsync(Vm vm, string newName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        // Guard 1: a linked clone embeds an ABSOLUTE backing path into this VM's folder, so moving the folder
        // would point the clone at a path that no longer exists — irreparable. Reuse the battle-tested
        // detection (qcow2 backing scan) rather than re-implementing it.
        IReadOnlyList<Vm> dependents = await _deletion.FindDependentsAsync(vm, cancellationToken);
        if (dependents.Count > 0)
        {
            string names = string.Join(", ", dependents.Select(d => $"'{d.Config.Name}'"));
            throw new VmHasDependentsException(
                $"Can't rename '{vm.Config.Name}': {dependents.Count} linked clone(s) are backed by its folder ({names}). " +
                "Their backing paths point at this folder, so moving it would corrupt them. Delete those first (or make them full clones).",
                [.. dependents.Select(d => d.Config.Name)]);
        }

        // Guard 2: a running VM holds open handles (disks, NVRAM, runtime.json); Directory.Move would fail or
        // half-complete. Core can only see runtime.json PRESENCE, not whether the recorded PID is truly alive
        // (that PID-verified check lives in the CLI's VmStatusProbe, which Core can't reach). So this is a
        // belt-and-braces guard; the CLI command does the authoritative liveness check before calling in.
        if (_runtimeStore.TryLoad(vm) is not null)
        {
            throw new InvalidOperationException(
                $"Can't rename '{vm.Config.Name}' while it has live runtime state (it may be running). Stop it first.");
        }

        // Write the new name into the CURRENT folder first, so vm.json is correct regardless of whether the
        // move below runs (a re-rename to the same slug short-circuits the move).
        VmConfig renamed = vm.Config with { Name = newName.Trim() };
        var atOldFolder = new Vm(vm.FolderPath, renamed);
        await _repository.SaveAsync(atOldFolder, cancellationToken);

        IReadOnlyList<string> taken = (await _repository.ListAsync(cancellationToken))
            .Where(other => !string.Equals(other.Config.Id, vm.Config.Id, StringComparison.Ordinal))
            .Select(other => Path.GetFileName(other.FolderPath.TrimEnd(Path.DirectorySeparatorChar)))
            .ToList();

        string slug = ComputeSlug(renamed.Name, taken, renamed.Id);
        if (string.Equals(Path.GetFileName(vm.FolderPath.TrimEnd(Path.DirectorySeparatorChar)), slug, FolderComparison))
        {
            return atOldFolder; // folder already matches the slug — name update only, no move needed
        }

        // runtime.json stores a PID, not a path, and the VM is stopped (guarded above), so it is normally
        // absent here; nothing path-bound needs rewriting after the move. Directory.Move carries any stray
        // file along with the rest of the folder atomically.
        return await _repository.MoveFolderAsync(atOldFolder, slug, cancellationToken);
    }

    /// <inheritdoc />
    public string ComputeSlug(string name, IEnumerable<string> takenFolderNames, string id)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(takenFolderNames);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        // The id suffix guarantees uniqueness even when two VMs share a display name, and keeps the folder
        // traceable back to its VM. Eight hex chars (the GUID's first block) is plenty to disambiguate.
        string idSuffix = Sanitize(id);
        idSuffix = idSuffix.Length > 8 ? idSuffix[..8] : idSuffix;
        if (idSuffix.Length == 0)
        {
            idSuffix = "vm"; // an all-invalid id can't happen for a GUID, but never produce an empty slug
        }

        string namePart = Kebab(name);
        string baseSlug = namePart.Length == 0 ? idSuffix : $"{namePart}-{idSuffix}";

        var taken = new HashSet<string>(takenFolderNames, OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);
        if (!taken.Contains(baseSlug))
        {
            return baseSlug;
        }

        // Collision (e.g. two VMs whose ids happen to share the first 8 hex on a case-insensitive FS):
        // append a numeric suffix until free.
        for (int n = 2; ; n++)
        {
            string candidate = $"{baseSlug}-{n.ToString(CultureInfo.InvariantCulture)}";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    // Lowercase kebab-case of the readable name: runs of non-[a-z0-9] collapse to a single '-', leading and
    // trailing dashes are trimmed, the result is length-capped, and a Windows reserved device name is prefixed
    // out of the way. Empty when the name has nothing slug-able (the caller falls back to the id suffix).
    private static string Kebab(string name)
    {
        var sb = new StringBuilder(name.Length);
        bool lastWasDash = false;
        foreach (char c in name.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                sb.Append(c);
                lastWasDash = false;
            }
            else if (!lastWasDash && sb.Length > 0)
            {
                sb.Append('-');
                lastWasDash = true;
            }
        }

        string slug = sb.ToString().Trim('-');
        if (slug.Length > MaxNameSlugLength)
        {
            slug = slug[..MaxNameSlugLength].TrimEnd('-');
        }

        // Kebab only emits [a-z0-9-], so InvalidNameChars are already gone; a reserved device name (e.g. a VM
        // literally named "con") would still be a problem on Windows, so guard it.
        return ReservedDeviceNames.Contains(slug) ? $"vm-{slug}" : slug;
    }

    // Strips characters that are invalid in a path segment on any target OS — used on the id suffix, which is
    // a GUID (already safe) but sanitized defensively in case a caller-supplied id ever isn't.
    private static string Sanitize(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (char c in value.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c) && Array.IndexOf(InvalidNameChars, c) < 0)
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}

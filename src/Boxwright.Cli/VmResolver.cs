using Boxwright.Core;

namespace Boxwright.Cli;

/// <summary>
/// Resolves a user-supplied VM reference — an exact id, an exact (case-insensitive) name,
/// or a unique id prefix — to a single <see cref="Vm"/>. Ambiguity and misses become clean
/// <see cref="CliException"/>s rather than a stack trace.
/// </summary>
internal sealed class VmResolver
{
    private readonly VmRepository _repository;

    public VmResolver(VmRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    /// <summary>Resolves <paramref name="reference"/> against the VMs on disk.</summary>
    /// <exception cref="CliException">No VM matches, or the reference is ambiguous.</exception>
    public async Task<Vm> ResolveAsync(string reference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new CliException("A VM id or name is required.");
        }

        IReadOnlyList<Vm> vms = await _repository.ListAsync(cancellationToken);

        // 1. Exact id wins outright.
        Vm? exactId = vms.FirstOrDefault(v => string.Equals(v.Config.Id, reference, StringComparison.Ordinal));
        if (exactId is not null)
        {
            return exactId;
        }

        // 2. Exact name (case-insensitive). Duplicate names are possible, so disambiguate by id.
        List<Vm> nameMatches = vms
            .Where(v => string.Equals(v.Config.Name, reference, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (nameMatches.Count == 1)
        {
            return nameMatches[0];
        }

        if (nameMatches.Count > 1)
        {
            throw new CliException(
                $"'{reference}' matches {nameMatches.Count} VMs by name; use the id instead:\n" +
                string.Join('\n', nameMatches.Select(v => $"  {v.Config.Id}  {v.Config.Name}")));
        }

        // 3. Unique id prefix (lets users type the first few chars of a GUID).
        List<Vm> prefixMatches = vms
            .Where(v => v.Config.Id.StartsWith(reference, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (prefixMatches.Count == 1)
        {
            return prefixMatches[0];
        }

        if (prefixMatches.Count > 1)
        {
            throw new CliException(
                $"'{reference}' is an ambiguous id prefix ({prefixMatches.Count} matches); type more characters.");
        }

        throw new CliException($"No VM matches '{reference}'. Run 'boxwright list' to see them.");
    }
}

namespace Boxwright.Core;

/// <summary>
/// Thrown when a VM can't be deleted because other VMs are <b>linked clones</b> backed by its disks
/// (ADR-0025): deleting it would pull the backing image out from under them and corrupt them. The
/// caller should delete (or unlink) the dependents first. Carries their names for a helpful message.
/// </summary>
public sealed class VmHasDependentsException : Exception
{
    /// <summary>The display names of the VMs that depend on the one being deleted.</summary>
    public IReadOnlyList<string> DependentNames { get; }

    /// <summary>Creates the exception with a message and the dependents' display names.</summary>
    public VmHasDependentsException(string message, IReadOnlyList<string> dependentNames)
        : base(message)
    {
        DependentNames = dependentNames ?? [];
    }
}

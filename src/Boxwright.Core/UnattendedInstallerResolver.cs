namespace Boxwright.Core;

/// <summary>Picks the <see cref="IUnattendedInstaller"/> for an OS family.</summary>
public interface IUnattendedInstallerResolver
{
    /// <summary>Returns the installer registered for <paramref name="osFamily"/>.</summary>
    /// <exception cref="InstallMediaException">No installer is registered for that family.</exception>
    IUnattendedInstaller Resolve(string osFamily);
}

/// <summary>
/// Resolves an <see cref="IUnattendedInstaller"/> from the set registered in DI, keyed on
/// <see cref="IUnattendedInstaller.OsFamily"/> (case-insensitive). Keeps the create flow
/// family-driven so a new family is just one more registered installer.
/// </summary>
public sealed class UnattendedInstallerResolver : IUnattendedInstallerResolver
{
    private readonly IReadOnlyList<IUnattendedInstaller> _installers;

    public UnattendedInstallerResolver(IEnumerable<IUnattendedInstaller> installers)
    {
        ArgumentNullException.ThrowIfNull(installers);
        _installers = [.. installers];
    }

    /// <inheritdoc />
    public IUnattendedInstaller Resolve(string osFamily) =>
        _installers.FirstOrDefault(i => string.Equals(i.OsFamily, osFamily, StringComparison.OrdinalIgnoreCase))
        ?? throw new InstallMediaException($"Unattended install isn't supported for the '{osFamily}' OS family.");
}

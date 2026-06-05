namespace Boxwright.Core;

/// <summary>
/// Prepares a hands-free unattended install for one OS family (Ubuntu autoinstall, Debian preseed, …).
/// Each implementation knows that family's installer layout (where the kernel/initrd live), how to
/// build its answer file, and which kernel command line makes it run non-interactively. Resolved by
/// <see cref="IUnattendedInstallerResolver"/> from <see cref="OsCatalogEntry.OsFamily"/>.
/// </summary>
public interface IUnattendedInstaller
{
    /// <summary>The OS family this installer handles, matched against <see cref="OsCatalogEntry.OsFamily"/> (e.g. <c>ubuntu</c>, <c>debian</c>).</summary>
    string OsFamily { get; }

    /// <summary>
    /// Prepares the installer media in <paramref name="vmFolderPath"/> (extract kernel/initrd, write
    /// any seed) for the ISO at <paramref name="isoPath"/> and returns the <see cref="UnattendedInstallPlan"/>
    /// (how to boot it + any seed disks to attach).
    /// </summary>
    /// <exception cref="InstallMediaException">The ISO is unreadable or isn't a recognizable installer for this family.</exception>
    UnattendedInstallPlan Prepare(string isoPath, string vmFolderPath, UnattendedAnswers answers);
}

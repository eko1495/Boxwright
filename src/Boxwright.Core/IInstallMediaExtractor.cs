namespace Boxwright.Core;

/// <summary>
/// Extracts the bootable kernel + initrd from an installer ISO and produces an
/// <see cref="InstallBootConfig"/> for a hands-free unattended install (ADR-0013 Phase B). The default
/// implementation (<see cref="InstallMediaExtractor"/>) reads the ISO with DiscUtils — no external tool.
/// </summary>
public interface IInstallMediaExtractor
{
    /// <summary>
    /// Extracts the installer kernel + initrd from <paramref name="isoPath"/> into
    /// <paramref name="vmFolderPath"/> and returns an <see cref="InstallBootConfig"/> whose
    /// <see cref="InstallBootConfig.Append"/> is <paramref name="seedArgs"/> (e.g. <c>autoinstall
    /// ds=nocloud</c>) followed by the ISO's own kernel command line.
    /// </summary>
    /// <exception cref="InstallMediaException">The ISO is unreadable or has no recognizable installer kernel/initrd.</exception>
    InstallBootConfig Extract(string isoPath, string vmFolderPath, string seedArgs);
}

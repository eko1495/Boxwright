namespace Boxwright.Core;

/// <summary>
/// Generates the Windows <c>Autounattend.xml</c> seed CD for an unattended install and returns the
/// path to the ISO. The default implementation (<see cref="AutounattendSeedGenerator"/>) writes a small
/// ISO9660/Joliet image holding <c>Autounattend.xml</c> at its root, attached to the VM as an extra
/// CD-ROM where Windows Setup auto-discovers it (see ADR-0015).
/// </summary>
public interface IAutounattendSeedGenerator
{
    /// <summary>
    /// Writes the autounattend ISO into <paramref name="vmFolderPath"/> and returns its absolute path.
    /// <paramref name="uefi"/> selects the GPT (UEFI) vs MBR (BIOS) disk layout in the answer file.
    /// </summary>
    string Generate(UnattendedAnswers answers, WindowsInstallOptions options, bool uefi, string vmFolderPath);
}

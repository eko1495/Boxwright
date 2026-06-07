namespace Boxwright.Core;

/// <summary>
/// Generates an unattended-install seed for a VM and returns the path to the seed image. The default
/// implementation (<see cref="CloudInitSeedGenerator"/>) writes a cloud-init NoCloud FAT image
/// (volume label <c>CIDATA</c>) that Ubuntu's autoinstall picks up when it is attached to the VM.
/// </summary>
public interface ISeedGenerator
{
    /// <summary>
    /// Writes a seed image into <paramref name="vmFolderPath"/> and returns its absolute path. The
    /// <paramref name="profile"/> selects which cloud-init <c>user-data</c> is baked in (installer
    /// autoinstall vs. a pre-installed cloud image).
    /// </summary>
    string Generate(UnattendedAnswers answers, string vmFolderPath, SeedProfile profile = SeedProfile.InstallerAutoinstall);
}

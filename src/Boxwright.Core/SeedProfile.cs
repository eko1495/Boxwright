namespace Boxwright.Core;

/// <summary>
/// Selects which cloud-init <c>user-data</c> a <see cref="ISeedGenerator"/> bakes into the NoCloud
/// seed. The seed mechanics (a FAT <c>CIDATA</c> volume) are identical; only the document differs.
/// </summary>
public enum SeedProfile
{
    /// <summary>
    /// Ubuntu subiquity <b>autoinstall</b> (<see cref="AutoinstallUserData"/>) — for the live-server
    /// installer ISO path. Experimental: still needs an <c>autoinstall</c> kernel arg to run fully
    /// hands-free (ADR-0013, Phase B).
    /// </summary>
    InstallerAutoinstall,

    /// <summary>
    /// Plain cloud-init <see cref="CloudImageUserData"/> — for a pre-installed cloud image (qcow2)
    /// that runs cloud-init on first boot. No installer; the seed sets up the login.
    /// </summary>
    CloudImage,
}

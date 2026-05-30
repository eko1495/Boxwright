namespace Boxwright.Core;

/// <summary>
/// A VM as it lives on disk: its folder plus the loaded <see cref="VmConfig"/>.
/// One VM is one self-contained folder (config + disks + logs); copying the
/// folder moves the VM (ADR-0006).
/// </summary>
/// <param name="FolderPath">The VM's folder.</param>
/// <param name="Config">The VM's configuration.</param>
public sealed record Vm(string FolderPath, VmConfig Config)
{
    /// <summary>Full path to the VM's JSON config file within its folder.</summary>
    public string ConfigPath => Path.Combine(FolderPath, VmRepository.ConfigFileName);

    /// <summary>The QEMU stdout/stderr log file name inside each VM folder (overwritten each launch).</summary>
    public const string LogFileName = "qemu.log";

    /// <summary>Full path to the VM's QEMU log within its folder.</summary>
    public string LogPath => Path.Combine(FolderPath, LogFileName);
}

namespace Boxwright.Core;

/// <summary>
/// What a family-specific <see cref="IUnattendedInstaller"/> produced for a VM: how to boot the
/// installer (<see cref="Boot"/>) and any seed disks to attach (<see cref="SeedDisks"/>). Ubuntu
/// returns a cloud-init <c>CIDATA</c> seed disk; Debian returns none (its preseed lives inside the
/// installer initrd). The caller appends <see cref="SeedDisks"/> to the VM's disks and sets
/// <see cref="VmConfig.InstallBoot"/> to <see cref="Boot"/>.
/// </summary>
public sealed record UnattendedInstallPlan
{
    /// <summary>The one-shot direct-kernel boot for the installer (ADR-0013 Phase B).</summary>
    public InstallBootConfig Boot { get; init; } = new();

    /// <summary>Extra seed disks to attach (e.g. a cloud-init <c>CIDATA</c> image); empty if none.</summary>
    public IReadOnlyList<DiskConfig> SeedDisks { get; init; } = [];
}

namespace Boxwright.Core;

/// <summary>
/// How much <c>qemu-img check -r</c> should try to repair on a disk image. Repair rewrites the image and
/// can <b>discard</b> data it can't recover (e.g. dropping references to unrecoverable corrupt clusters),
/// so it is never the default — the caller opts in explicitly.
/// </summary>
public enum DiskRepairMode
{
    /// <summary>Read-only check; never writes to the image (<c>qemu-img check</c>).</summary>
    None,

    /// <summary>Repair only leaked clusters — always safe, just reclaims wasted space (<c>-r leaks</c>).</summary>
    Leaks,

    /// <summary>Repair leaks and corruptions (<c>-r all</c>) — may discard unrecoverable data.</summary>
    All,
}

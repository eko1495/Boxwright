namespace Boxwright.Core;

/// <summary>
/// A QEMU acceleration backend. Selected per-host at launch and never hardcoded
/// (Directive 5 / ADR-0003).
/// </summary>
public enum Accelerator
{
    /// <summary>Linux KVM.</summary>
    Kvm,

    /// <summary>macOS Hypervisor.framework.</summary>
    Hvf,

    /// <summary>Windows Hypervisor Platform.</summary>
    Whpx,

    /// <summary>Pure software emulation (TCG) — the universal fallback.</summary>
    Tcg,
}

/// <summary>Helpers for <see cref="Accelerator"/>.</summary>
public static class AcceleratorExtensions
{
    /// <summary>
    /// The value to pass to QEMU's <c>-accel</c> option (<c>kvm</c>, <c>hvf</c>,
    /// <c>whpx</c>, or <c>tcg</c>). Note: GATE-0 found WHPX runs cleaner with
    /// <c>whpx,kernel-irqchip=off</c>; that option is applied by the command-line
    /// builder (CORE-5), not encoded here.
    /// </summary>
    public static string ToQemuValue(this Accelerator accelerator) => accelerator switch
    {
        Accelerator.Kvm => "kvm",
        Accelerator.Hvf => "hvf",
        Accelerator.Whpx => "whpx",
        Accelerator.Tcg => "tcg",
        _ => throw new ArgumentOutOfRangeException(nameof(accelerator), accelerator, "Unknown accelerator."),
    };
}

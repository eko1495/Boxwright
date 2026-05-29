namespace Boxwright.Core;

/// <summary>
/// Selects the host's QEMU accelerator at launch — never hardcoded (Directive 5 /
/// ADR-0003). Prefers the platform's hardware accelerator (KVM on Linux, HVF on
/// macOS, WHPX on Windows) and falls back to TCG (software) when none is available.
/// </summary>
public sealed class AcceleratorDetector
{
    private readonly IHostAccelerationProbe _probe;

    /// <summary>Creates a detector over the given host probe.</summary>
    public AcceleratorDetector(IHostAccelerationProbe probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        _probe = probe;
    }

    /// <summary>Creates a detector using the default host probe.</summary>
    public static AcceleratorDetector CreateDefault() => new(new DefaultHostAccelerationProbe());

    /// <summary>
    /// Resolves the accelerator to use for this host, falling back to
    /// <see cref="Accelerator.Tcg"/> when no hardware accelerator is available.
    /// </summary>
    public Accelerator Detect()
    {
        if (_probe.IsKvmAvailable())
        {
            return Accelerator.Kvm;
        }

        if (_probe.IsHvfAvailable())
        {
            return Accelerator.Hvf;
        }

        if (_probe.IsWhpxAvailable())
        {
            return Accelerator.Whpx;
        }

        return Accelerator.Tcg;
    }
}

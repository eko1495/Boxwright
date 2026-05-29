namespace Boxwright.Core;

/// <summary>
/// Probes the host for hardware-acceleration availability. Each method is
/// OS-specific and returns false off its platform, so the detector can check them
/// in order. Injected so detection is unit-testable without depending on the dev
/// machine's real capabilities.
/// </summary>
public interface IHostAccelerationProbe
{
    /// <summary>True on Linux when KVM (<c>/dev/kvm</c>) is available.</summary>
    bool IsKvmAvailable();

    /// <summary>True on macOS when Hypervisor.framework (HVF) is available.</summary>
    bool IsHvfAvailable();

    /// <summary>True on Windows when the Windows Hypervisor Platform (WHPX) is available.</summary>
    bool IsWhpxAvailable();
}

/// <summary>
/// The default <see cref="IHostAccelerationProbe"/>, using lightweight, admin-free
/// OS checks.
/// </summary>
public sealed class DefaultHostAccelerationProbe : IHostAccelerationProbe
{
    /// <inheritdoc />
    public bool IsKvmAvailable() => OperatingSystem.IsLinux() && File.Exists("/dev/kvm");

    /// <inheritdoc />
    // HVF is present on all QEMU-supported macOS (Intel HAXM is gone). A sysctl
    // kern.hv_support probe could refine this; assuming HVF on macOS is fine for MVP.
    public bool IsHvfAvailable() => OperatingSystem.IsMacOS();

    /// <inheritdoc />
    // WinHvPlatform.dll is installed in System32 when the "Windows Hypervisor
    // Platform" feature is enabled — an admin-free proxy for WHPX availability
    // (consistent with the GATE-0 box, where WHPX worked). The definitive check is
    // a trial launch (GATE-0); that refinement is deferred.
    public bool IsWhpxAvailable() =>
        OperatingSystem.IsWindows()
        && File.Exists(Path.Combine(Environment.SystemDirectory, "WinHvPlatform.dll"));
}

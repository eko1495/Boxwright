namespace Boxwright.Core;

/// <summary>
/// Host-capability checks for a VM's networking (ADR-0024). Bridged and TAP networking are Linux-only;
/// this fails a launch fast with a clear message rather than letting QEMU emit a baffling error when a
/// bridge/tap VM is started on (or copied to) a non-Linux host.
/// </summary>
public static class NetworkValidation
{
    /// <summary>Network modes that require a Linux host.</summary>
    public static bool RequiresLinux(string mode) =>
        string.Equals(mode, "bridge", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(mode, "tap", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Throws <see cref="VmConfigException"/> when <paramref name="network"/> uses a Linux-only mode and
    /// <paramref name="isLinux"/> is false. Pure (the OS is passed in) so it is unit-testable on any host.
    /// </summary>
    public static void EnsureSupportedOnHost(NetworkConfig network, bool isLinux)
    {
        ArgumentNullException.ThrowIfNull(network);
        if (RequiresLinux(network.Mode) && !isLinux)
        {
            throw new VmConfigException(
                $"'{network.Mode}' networking is only supported on Linux. Use user-mode networking on this host, " +
                "or run the VM on a Linux host with the bridge/TAP set up.");
        }
    }

    /// <summary>Convenience overload that checks against the current OS.</summary>
    public static void EnsureSupportedOnHost(NetworkConfig network) =>
        EnsureSupportedOnHost(network, OperatingSystem.IsLinux());
}

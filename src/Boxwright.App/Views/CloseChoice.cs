namespace Boxwright.App.Views;

/// <summary>
/// What to do with running VMs when the user closes Boxwright. <see cref="Cancel"/> is the
/// default (value 0) so a dismissed dialog safely keeps the app open rather than acting.
/// </summary>
public enum CloseChoice
{
    /// <summary>Keep Boxwright open; don't touch the VMs.</summary>
    Cancel,

    /// <summary>Send each guest an ACPI power-off and wait for it to finish.</summary>
    ShutDown,

    /// <summary>Terminate each VM immediately (pull the plug).</summary>
    ForceOff,
}

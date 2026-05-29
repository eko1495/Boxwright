namespace Boxwright.Core;

/// <summary>
/// Thrown when a required QEMU binary (<c>qemu-system-*</c> or <c>qemu-img</c>)
/// cannot be located in the bundled directory or on the system PATH.
/// </summary>
public sealed class QemuNotFoundException : Exception
{
    /// <summary>Creates an exception with no message.</summary>
    public QemuNotFoundException()
    {
    }

    /// <summary>Creates an exception with the given <paramref name="message"/>.</summary>
    public QemuNotFoundException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with the given <paramref name="message"/> and <paramref name="innerException"/>.</summary>
    public QemuNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

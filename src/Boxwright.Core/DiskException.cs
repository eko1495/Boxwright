namespace Boxwright.Core;

/// <summary>Thrown when a <c>qemu-img</c> disk operation fails or its output cannot be parsed.</summary>
public sealed class DiskException : Exception
{
    /// <summary>Creates an exception with no message.</summary>
    public DiskException()
    {
    }

    /// <summary>Creates an exception with the given <paramref name="message"/>.</summary>
    public DiskException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with the given <paramref name="message"/> and <paramref name="innerException"/>.</summary>
    public DiskException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

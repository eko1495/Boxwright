namespace Boxwright.Core;

/// <summary>Thrown when the VM display cannot be opened — e.g. <c>remote-viewer</c> is not installed.</summary>
public sealed class DisplayException : Exception
{
    /// <summary>Creates an exception with no message.</summary>
    public DisplayException()
    {
    }

    /// <summary>Creates an exception with the given <paramref name="message"/>.</summary>
    public DisplayException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with the given <paramref name="message"/> and <paramref name="innerException"/>.</summary>
    public DisplayException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

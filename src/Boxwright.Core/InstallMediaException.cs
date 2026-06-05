namespace Boxwright.Core;

/// <summary>Thrown when an installer ISO cannot be read or has no recognizable kernel/initrd to extract.</summary>
public sealed class InstallMediaException : Exception
{
    /// <summary>Creates an exception with no message.</summary>
    public InstallMediaException()
    {
    }

    /// <summary>Creates an exception with the given <paramref name="message"/>.</summary>
    public InstallMediaException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with the given <paramref name="message"/> and <paramref name="innerException"/>.</summary>
    public InstallMediaException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

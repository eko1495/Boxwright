namespace Boxwright.Core;

/// <summary>
/// Thrown when a VM config cannot be loaded — malformed JSON, or an unsupported
/// <c>schemaVersion</c>.
/// </summary>
public sealed class VmConfigException : Exception
{
    /// <summary>Creates an exception with no message.</summary>
    public VmConfigException()
    {
    }

    /// <summary>Creates an exception with the given <paramref name="message"/>.</summary>
    public VmConfigException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with the given <paramref name="message"/> and <paramref name="innerException"/>.</summary>
    public VmConfigException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

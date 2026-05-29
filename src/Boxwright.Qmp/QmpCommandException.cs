namespace Boxwright.Qmp;

/// <summary>
/// Thrown when QEMU replies to a command with a QMP <c>error</c> object, for
/// example <c>{"error":{"class":"CommandNotFound","desc":"…"}}</c>.
/// </summary>
public sealed class QmpCommandException : Exception
{
    /// <summary>Creates an exception carrying the QMP <paramref name="errorClass"/> and <paramref name="description"/>.</summary>
    public QmpCommandException(string errorClass, string description)
        : base($"QMP command failed [{errorClass}]: {description}")
    {
        ErrorClass = errorClass;
        Description = description;
    }

    /// <summary>Creates an exception with no error detail.</summary>
    public QmpCommandException()
    {
        ErrorClass = string.Empty;
        Description = string.Empty;
    }

    /// <summary>Creates an exception with the given <paramref name="message"/>.</summary>
    public QmpCommandException(string message)
        : base(message)
    {
        ErrorClass = string.Empty;
        Description = message;
    }

    /// <summary>Creates an exception with the given <paramref name="message"/> and <paramref name="innerException"/>.</summary>
    public QmpCommandException(string message, Exception innerException)
        : base(message, innerException)
    {
        ErrorClass = string.Empty;
        Description = message;
    }

    /// <summary>The QMP error class, e.g. <c>"GenericError"</c> or <c>"CommandNotFound"</c>.</summary>
    public string ErrorClass { get; }

    /// <summary>The human-readable error description reported by QEMU.</summary>
    public string Description { get; }
}

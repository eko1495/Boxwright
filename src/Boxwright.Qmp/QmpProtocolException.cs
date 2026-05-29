namespace Boxwright.Qmp;

/// <summary>
/// Thrown when the QMP connection or protocol is violated — a missing or
/// malformed greeting, a malformed reply, or the connection closing
/// unexpectedly. Distinct from <see cref="QmpCommandException"/>, which signals
/// a QMP <c>error</c> reply to an otherwise well-formed command.
/// </summary>
public sealed class QmpProtocolException : Exception
{
    /// <summary>Creates an exception with no message.</summary>
    public QmpProtocolException()
    {
    }

    /// <summary>Creates an exception with the given <paramref name="message"/>.</summary>
    public QmpProtocolException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with the given <paramref name="message"/> and <paramref name="innerException"/>.</summary>
    public QmpProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

namespace Boxwright.Core;

/// <summary>
/// Thrown when an OpenPGP signature can't be processed — malformed signature/key data, or no public key
/// in the supplied key material matches the signature. A signature that processes cleanly but doesn't
/// verify is <b>not</b> an exception: it's a <see cref="OpenPgpVerification.IsValid"/> = <c>false</c> result.
/// </summary>
public sealed class OpenPgpException : Exception
{
    /// <summary>Creates the exception with a message.</summary>
    public OpenPgpException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the underlying cause.</summary>
    public OpenPgpException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

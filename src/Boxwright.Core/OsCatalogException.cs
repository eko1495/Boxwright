namespace Boxwright.Core;

/// <summary>Thrown when the OS catalog cannot be loaded or its JSON is malformed or an unsupported schema version.</summary>
public sealed class OsCatalogException : Exception
{
    /// <summary>Creates an exception with no message.</summary>
    public OsCatalogException()
    {
    }

    /// <summary>Creates an exception with the given <paramref name="message"/>.</summary>
    public OsCatalogException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with the given <paramref name="message"/> and <paramref name="innerException"/>.</summary>
    public OsCatalogException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

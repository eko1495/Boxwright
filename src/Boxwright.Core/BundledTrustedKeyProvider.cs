using System.Reflection;

namespace Boxwright.Core;

/// <summary>
/// The default <see cref="ITrustedKeyProvider"/>: resolves trusted OpenPGP public keys from armored
/// <c>.asc</c> files embedded in this assembly under <c>keys/</c> (ADR-0027). A key id maps to the
/// manifest resource <c>Boxwright.Core.keys.&lt;keyId&gt;.asc</c>, so the bundled trust anchors are part
/// of the installed binary rather than anything fetched at runtime.
/// </summary>
/// <remarks>
/// PHASE 2 status (ADR-0027): the resolution mechanism is implemented and unit-tested with a throwaway
/// key, but <b>no real distro release keys are bundled yet</b> — the <c>keys/</c> folder ships only a
/// placeholder. Bundling actual release keys and pointing a catalog entry at a live signature is the
/// explicitly-deferred live-verification step (it needs real key material plus an end-to-end download).
/// </remarks>
public sealed class BundledTrustedKeyProvider : ITrustedKeyProvider
{
    private const string ResourcePrefix = "Boxwright.Core.keys.";
    private const string ResourceSuffix = ".asc";

    private readonly Assembly _assembly;

    /// <summary>Creates a provider reading from this assembly's embedded <c>keys/</c> resources.</summary>
    public BundledTrustedKeyProvider()
        : this(typeof(BundledTrustedKeyProvider).Assembly)
    {
    }

    // The assembly seam exists for symmetry with other Core sources; production always uses the default.
    internal BundledTrustedKeyProvider(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        _assembly = assembly;
    }

    /// <inheritdoc />
    public Stream? OpenPublicKey(string keyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);

        // A catalog entry's KeyId is untrusted text; restrict it to a safe key-name charset (letters,
        // digits, dot, dash, underscore) so it can't reach for a different embedded resource. Dots are
        // allowed so real release-key ids like "canonical.archive" resolve; path separators and "<..>"
        // parent refs are rejected.
        if (keyId.Contains("..", StringComparison.Ordinal) ||
            keyId.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_')))
        {
            return null;
        }

        return _assembly.GetManifestResourceStream(ResourcePrefix + keyId + ResourceSuffix);
    }
}

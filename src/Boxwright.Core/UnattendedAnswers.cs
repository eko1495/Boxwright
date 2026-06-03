namespace Boxwright.Core;

/// <summary>
/// User-supplied answers for an unattended OS install. <see cref="AutoinstallUserData"/> turns these
/// into the cloud-init <c>user-data</c> that <see cref="CloudInitSeedGenerator"/> bakes into the
/// NoCloud seed. Defaults cover everything except the credentials, which the UI collects.
/// </summary>
public sealed record UnattendedAnswers
{
    /// <summary>The guest hostname.</summary>
    public string Hostname { get; init; } = "boxwright";

    /// <summary>The primary user account name.</summary>
    public string Username { get; init; } = "user";

    /// <summary>
    /// The primary user's plaintext password. It is hashed (SHA-512 crypt) into the seed —
    /// the plaintext is never written to the seed.
    /// </summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>Locale, e.g. <c>en_US.UTF-8</c>.</summary>
    public string Locale { get; init; } = "en_US.UTF-8";

    /// <summary>IANA timezone, e.g. <c>UTC</c> or <c>Europe/Warsaw</c>.</summary>
    public string Timezone { get; init; } = "UTC";

    /// <summary>Keyboard layout, e.g. <c>us</c>.</summary>
    public string KeyboardLayout { get; init; } = "us";
}

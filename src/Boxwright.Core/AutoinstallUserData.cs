namespace Boxwright.Core;

/// <summary>
/// Builds the cloud-init <c>user-data</c> (a <c>#cloud-config</c> with an <c>autoinstall:</c> block)
/// for an Ubuntu unattended install, plus the matching <c>meta-data</c>. Emitted from a fixed YAML
/// template with substituted values — no YAML library needed. The password is embedded only as a
/// SHA-512 crypt hash (see <see cref="Sha512Crypt"/>); the plaintext never reaches the seed.
/// </summary>
public static class AutoinstallUserData
{
    /// <summary>Builds the Ubuntu autoinstall <c>user-data</c> document for the given answers.</summary>
    public static string Build(UnattendedAnswers answers)
    {
        ArgumentNullException.ThrowIfNull(answers);

        string passwordHash = Sha512Crypt.Hash(answers.Password);

        // The seed pre-answers the whole install. subiquity still shows ONE disk-erase confirmation
        // unless the `autoinstall` kernel parameter is set (ADR-0013, Phase B). `match: size: largest`
        // installs onto the real disk, never the tiny CIDATA seed disk. `shutdown -P now` powers the
        // VM off when the install finishes, so it doesn't reboot straight back into the installer.
        return $"""
            #cloud-config
            autoinstall:
              version: 1
              locale: {answers.Locale}
              timezone: {answers.Timezone}
              keyboard:
                layout: {answers.KeyboardLayout}
              identity:
                hostname: {answers.Hostname}
                username: {answers.Username}
                password: "{passwordHash}"
              storage:
                layout:
                  name: lvm
                  match:
                    size: largest
              late-commands:
                - shutdown -P now

            """;
    }

    /// <summary>The minimal NoCloud <c>meta-data</c> document (just a unique instance id).</summary>
    public static string MetaData(string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        return $"instance-id: {instanceId}\n";
    }
}

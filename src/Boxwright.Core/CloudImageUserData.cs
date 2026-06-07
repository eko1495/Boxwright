namespace Boxwright.Core;

/// <summary>
/// Builds the cloud-init <c>user-data</c> (a plain <c>#cloud-config</c>) for a pre-installed
/// <b>cloud image</b> (qcow2) — Ubuntu, etc. Unlike <see cref="AutoinstallUserData"/> there is no
/// subiquity <c>autoinstall:</c> block: the OS is already installed, so cloud-init just creates the
/// login account and applies locale/timezone/keyboard on first boot. Emitted from a fixed YAML
/// template with substituted values — no YAML library needed. The password is embedded only as a
/// SHA-512 crypt hash (see <see cref="Sha512Crypt"/>); the plaintext never reaches the seed.
/// </summary>
public static class CloudImageUserData
{
    /// <summary>Builds the cloud-image <c>user-data</c> document for the given answers.</summary>
    public static string Build(UnattendedAnswers answers)
    {
        ArgumentNullException.ThrowIfNull(answers);

        string passwordHash = Sha512Crypt.Hash(answers.Password);

        // A cloud image ships no default password, so this seed is the ONLY way to log in. We define a
        // single sudo-capable user with a password (lock_passwd: false) and don't list `default`, so
        // cloud-init replaces the image's stock `ubuntu` user with the one chosen here. `chpasswd
        // expire: false` keeps the password from being expired-on-first-login. `ssh_pwauth: true`
        // allows password SSH over the user-mode net forward. cloud-init self-disables after the first
        // boot (it tracks the instance-id), so this runs exactly once.
        return $"""
            #cloud-config
            hostname: {answers.Hostname}
            locale: {answers.Locale}
            timezone: {answers.Timezone}
            keyboard:
              layout: {answers.KeyboardLayout}
            users:
              - name: {answers.Username}
                passwd: "{passwordHash}"
                lock_passwd: false
                shell: /bin/bash
                sudo: ALL=(ALL) NOPASSWD:ALL
                groups: [adm, sudo]
            ssh_pwauth: true
            chpasswd:
              expire: false

            """;
    }
}

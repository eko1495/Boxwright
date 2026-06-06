namespace Boxwright.Core;

/// <summary>
/// Builds an Anaconda <c>ks.cfg</c> kickstart for a fully unattended Fedora install (ADR-0017). Every
/// choice the installer would prompt for is pre-answered, so Anaconda runs with no interaction. The
/// kickstart is injected into the installer initrd (<see cref="InitrdFileInjector"/>) and selected with
/// <c>inst.ks=file:/ks.cfg</c> on the kernel command line. The user password is embedded only as a
/// SHA-512 crypt hash (<see cref="Sha512Crypt"/>); the plaintext never reaches the kickstart. Targets
/// the Workstation (GNOME) environment over the network — a netinst ISO carries no packages.
/// </summary>
public static class FedoraKickstart
{
    /// <summary>The file name the kickstart is injected as inside the installer initrd (referenced by <c>inst.ks=file:/ks.cfg</c>).</summary>
    public const string FileName = "ks.cfg";

    /// <summary>Builds the <c>ks.cfg</c> document for the given answers (Workstation/GNOME, guided autopart, power off at the end).</summary>
    public static string Build(UnattendedAnswers answers)
    {
        ArgumentNullException.ThrowIfNull(answers);

        string passwordHash = Sha512Crypt.Hash(answers.Password);

        // Notes:
        // - `url --mirrorlist` is required: the netinst ISO has no packages, so the repo is the Fedora mirrors.
        // - `@^workstation-product-environment` installs the GNOME desktop (a large network download).
        // - `poweroff` powers the VM off when the install finishes, so the app graduates it to a disk boot
        //   (VmListItemViewModel.OnSessionExited) instead of rebooting back into the installer.
        return $"""
            # Boxwright-generated Fedora kickstart (unattended Workstation install).
            text
            keyboard --vckeymap={answers.KeyboardLayout}
            lang {answers.Locale}
            timezone {answers.Timezone} --utc

            # Network (DHCP over QEMU user-mode networking).
            network --bootproto=dhcp --hostname={answers.Hostname}

            # Install source: the netinst ISO ships no packages, so pull from the Fedora mirrors.
            url --mirrorlist="https://mirrors.fedoraproject.org/mirrorlist?repo=fedora-$releasever&arch=$basearch"

            # Disk: wipe disk and let Anaconda auto-partition the whole drive (creates the ESP under UEFI).
            zerombr
            clearpart --all --initlabel
            autopart
            bootloader --location=mbr

            # Accounts: root login disabled; the user is a sudoer (wheel). Password is a SHA-512 crypt hash.
            rootpw --lock
            user --name={answers.Username} --groups=wheel --iscrypted --password={passwordHash}

            # Power off when the install finishes so Boxwright graduates the VM to a disk boot.
            poweroff

            %packages
            @^workstation-product-environment
            %end

            """;
    }
}

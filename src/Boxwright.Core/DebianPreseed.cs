namespace Boxwright.Core;

/// <summary>
/// Builds a debian-installer (d-i) <c>preseed.cfg</c> for a fully unattended Debian install. Every
/// prompt the installer would otherwise ask is pre-answered — including the partman disk-write
/// confirmations, which are Debian's equivalent of subiquity's "Review your choices" gate. The
/// preseed is injected into the installer initrd (<see cref="InitrdPreseedInjector"/>) and read
/// automatically from the initramfs root; <c>auto=true priority=critical</c> on the kernel command
/// line suppresses the early locale/keyboard/network questions. The user password is embedded only
/// as a SHA-512 crypt hash (<see cref="Sha512Crypt"/>); the plaintext never reaches the preseed.
/// </summary>
public static class DebianPreseed
{
    /// <summary>The file name the preseed is written as inside the installer initrd (d-i auto-loads <c>/preseed.cfg</c>).</summary>
    public const string FileName = "preseed.cfg";

    /// <summary>Builds the <c>preseed.cfg</c> document for the given answers (GNOME desktop, guided LVM, power off at the end).</summary>
    public static string Build(UnattendedAnswers answers)
    {
        ArgumentNullException.ThrowIfNull(answers);

        string passwordHash = Sha512Crypt.Hash(answers.Password);

        // Notes:
        // - tasksel `standard, gnome-desktop` gives a graphical desktop (downloaded over the network).
        // - The partman/* confirmations auto-accept the destructive disk write (the manual gate).
        // - `debian-installer/exit/poweroff true` powers the VM off when the install finishes, so the app
        //   graduates it to a normal disk boot (VmListItemViewModel.OnSessionExited) instead of looping
        //   back into the installer.
        return $"""
            ### Localization
            d-i debian-installer/locale string {answers.Locale}
            d-i keyboard-configuration/xkb-keymap select {answers.KeyboardLayout}

            ### Network
            d-i netcfg/choose_interface select auto
            d-i netcfg/get_hostname string {answers.Hostname}
            d-i netcfg/hostname string {answers.Hostname}
            d-i netcfg/get_domain string

            ### Apt mirror
            d-i mirror/country string manual
            d-i mirror/http/hostname string deb.debian.org
            d-i mirror/http/directory string /debian
            d-i mirror/http/proxy string

            ### Clock and time zone
            d-i clock-setup/utc boolean true
            d-i time/zone string {answers.Timezone}
            d-i clock-setup/ntp boolean true

            ### Account setup (root login disabled; the user is a sudoer)
            d-i passwd/root-login boolean false
            d-i passwd/user-fullname string {answers.Username}
            d-i passwd/username string {answers.Username}
            d-i passwd/user-password-crypted password {passwordHash}
            d-i user-setup/allow-password-weak boolean true
            d-i user-setup/encrypt-home boolean false

            ### Partitioning (guided, whole disk, LVM) — auto-confirm every write prompt
            d-i partman-auto/method string lvm
            d-i partman-lvm/device_remove_lvm boolean true
            d-i partman-md/device_remove_md boolean true
            d-i partman-lvm/confirm boolean true
            d-i partman-lvm/confirm_nooverwrite boolean true
            d-i partman-auto/choose_recipe select atomic
            d-i partman-partitioning/confirm_write_new_label boolean true
            d-i partman/choose_partition select finish
            d-i partman/confirm boolean true
            d-i partman/confirm_nooverwrite boolean true

            ### Base system + packages
            d-i apt-setup/non-free-firmware boolean true
            tasksel tasksel/first multiselect standard, gnome-desktop
            d-i pkgsel/upgrade select none
            popularity-contest popularity-contest/participate boolean false

            ### Boot loader
            d-i grub-installer/only_debian boolean true
            d-i grub-installer/with_other_os boolean true
            d-i grub-installer/bootdev string default

            ### Finish — power off so Boxwright graduates the VM to a disk boot
            d-i finish-install/reboot_in_progress note
            d-i debian-installer/exit/poweroff boolean true

            """;
    }
}

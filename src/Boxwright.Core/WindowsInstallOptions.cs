namespace Boxwright.Core;

/// <summary>
/// Windows-specific knobs for an unattended install, layered on top of the shared
/// <see cref="UnattendedAnswers"/> (which supplies hostname/username/password). Turned into an
/// <c>Autounattend.xml</c> by <see cref="AutounattendXml"/>. Defaults target a hands-free en-US install.
/// </summary>
public sealed record WindowsInstallOptions
{
    /// <summary>
    /// Optional product key. Leave empty for a single-edition or <b>Evaluation</b> ISO (Setup needs no
    /// key). For a multi-edition retail ISO, a per-edition <em>generic</em> key (see
    /// <see cref="GenericProKey"/>) lets Setup proceed without prompting; the VM installs unactivated.
    /// </summary>
    public string? ProductKey { get; init; }

    /// <summary>
    /// Which image in a multi-edition <c>install.wim</c>/<c>.esd</c> to install (pins the edition so
    /// Setup doesn't prompt). Index 1 is the first image; the exact index↔edition map is media-specific
    /// (<c>dism /Get-WimInfo</c>). Evaluation ISOs have a single image, so 1 is always correct.
    /// </summary>
    public int ImageIndex { get; init; } = 1;

    /// <summary>UI/system/user locale, e.g. <c>en-US</c> (Windows BCP-47 form — not the Linux <c>en_US.UTF-8</c>).</summary>
    public string Locale { get; init; } = "en-US";

    /// <summary>Keyboard input locale, e.g. <c>en-US</c> or <c>0409:00000409</c>.</summary>
    public string InputLocale { get; init; } = "en-US";

    /// <summary>Windows time-zone name, e.g. <c>UTC</c> or <c>W. Europe Standard Time</c> (not an IANA id).</summary>
    public string TimeZone { get; init; } = "UTC";

    /// <summary>
    /// When true, the install targets paravirtualized <b>virtio</b> devices (faster than the in-box SATA +
    /// e1000e). The Autounattend injects the virtio storage (and network) drivers from the attached
    /// virtio-win ISO so Setup can see the virtio-blk disk (ADR-0018). The VM's disk interface and NIC are
    /// set to virtio by the create flow; this flag only drives the driver injection.
    /// </summary>
    public bool UseVirtio { get; init; }

    /// <summary>The public, non-activating generic install key for Windows 10/11 <b>Pro</b> (a convenience default for retail multi-edition ISOs).</summary>
    public const string GenericProKey = "W269N-WFGWX-YVC9B-4J6C9-T83GX";
}

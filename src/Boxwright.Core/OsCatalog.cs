namespace Boxwright.Core;

/// <summary>
/// The OS catalog document: a versioned list of installable OS images. Deserialized
/// from the curated <c>OsCatalog.json</c> bundled with the app (see
/// <see cref="BundledOsCatalogSource"/>) via <see cref="OsCatalogJson"/>.
/// </summary>
public sealed record OsCatalogDocument
{
    /// <summary>The catalog schema version this build understands.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Schema version of the document (must equal <see cref="CurrentSchemaVersion"/>).</summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>The catalog entries.</summary>
    public IReadOnlyList<OsCatalogEntry> Entries { get; init; } = [];
}

/// <summary>
/// One installable OS image: where to download it, how to verify it, its provenance,
/// and the recommended VM specs the catalog prefills for it.
/// </summary>
public sealed record OsCatalogEntry
{
    /// <summary><see cref="ImageKind"/> value for a bootable installer ISO (the default).</summary>
    public const string ImageKindIso = "iso";

    /// <summary><see cref="ImageKind"/> value for a pre-installed cloud image (qcow2) consumed via cloud-init.</summary>
    public const string ImageKindCloudImage = "cloudimage";

    /// <summary>Stable identifier, e.g. <c>ubuntu-24.04-desktop</c>.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Display name, e.g. <c>Ubuntu Desktop</c>.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Human version label, e.g. <c>24.04.2 LTS</c>.</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>Guest architecture (e.g. <c>x86_64</c>).</summary>
    public string Arch { get; init; } = "x86_64";

    /// <summary>
    /// Whether <see cref="IsoUrl"/> points at an installer ISO (<see cref="ImageKindIso"/>, the
    /// default) or a pre-installed cloud image (<see cref="ImageKindCloudImage"/>). A cloud image is
    /// flattened into the VM as its disk and boots straight into cloud-init — no installer runs, so
    /// the unattended seed is required (it carries the only login the guest will have). See ADR-0013.
    /// </summary>
    public string ImageKind { get; init; } = ImageKindIso;

    /// <summary>Direct download URL (https) of the installer ISO or cloud image (see <see cref="ImageKind"/>).</summary>
    public Uri IsoUrl { get; init; } = null!;

    /// <summary>Expected SHA-256 of the ISO, lowercase hex.</summary>
    public string Sha256 { get; init; } = string.Empty;

    /// <summary>Expected download size in bytes (also used for cache-hit detection).</summary>
    public long SizeBytes { get; init; }

    /// <summary>Recommended VM specs for this OS.</summary>
    public OsRecommendedSpec Recommended { get; init; } = new();

    /// <summary>Human-readable provenance, e.g. <c>Canonical · releases.ubuntu.com</c>.</summary>
    public string SourceName { get; init; } = string.Empty;

    /// <summary>True when the OS needs a license the user must supply (e.g. a Windows evaluation).</summary>
    public bool RequiresLicense { get; init; }

    /// <summary>
    /// OS family, used to pick the unattended-install mechanism — e.g. <c>ubuntu</c> (cloud-init
    /// autoinstall), <c>debian</c> (preseed), <c>fedora</c> (kickstart). Empty if unspecified.
    /// </summary>
    public string OsFamily { get; init; } = string.Empty;

    /// <summary>
    /// True when Boxwright can generate an unattended-install seed for this entry. Currently this
    /// means Ubuntu autoinstall via a cloud-init NoCloud seed; other families install interactively
    /// (capability-gated — see ADR-0013).
    /// </summary>
    public bool SupportsAutoinstall { get; init; }

    /// <summary>Optional note shown to the user (e.g. evaluation terms or install hints).</summary>
    public string? Notes { get; init; }

    /// <summary>
    /// Optional declarative unattended-install recipe (ADR-0026). When present, an unattended install for
    /// this entry is driven by the recipe (<see cref="IRecipeInstaller"/>) instead of a built-in C# installer
    /// resolved by <see cref="OsFamily"/> — so the community can add a distro's unattended install as data.
    /// </summary>
    public UnattendedRecipe? Unattended { get; init; }

    /// <summary>
    /// Optional OpenPGP signature gate (ADR-0027). When present, a download is trusted only after its
    /// <see cref="Sha256"/> is found in a checksums document whose detached signature verifies against a
    /// <b>bundled</b> trusted key — provenance on top of integrity. When absent, behaviour is exactly as
    /// before: SHA-256 only. SHA-256 stays mandatory either way; the signature never replaces it.
    /// </summary>
    public OsCatalogSignature? Signature { get; init; }
}

/// <summary>
/// The optional OpenPGP signature gate for an <see cref="OsCatalogEntry"/> (ADR-0027). It names the
/// distro's checksums document, a detached signature over that document, and the id of the bundled
/// trusted key (<see cref="ITrustedKeyProvider"/>) expected to have signed it. The download is trusted
/// only when the signature verifies under that bundled key <em>and</em> the entry's
/// <see cref="OsCatalogEntry.Sha256"/> appears in the signed checksums against the expected filename.
/// </summary>
public sealed record OsCatalogSignature
{
    /// <summary>URL (https) of the distro's checksums document (e.g. a <c>SHA256SUMS</c> file).</summary>
    public Uri ChecksumsUrl { get; init; } = null!;

    /// <summary>URL (https) of the detached OpenPGP signature over <see cref="ChecksumsUrl"/> (e.g. <c>SHA256SUMS.gpg</c>).</summary>
    public Uri SignatureUrl { get; init; } = null!;

    /// <summary>
    /// Id of the bundled trusted public key expected to have signed the checksums — the name of the
    /// armored key under <c>keys/</c> (see <see cref="BundledTrustedKeyProvider"/>), resolved out of band.
    /// </summary>
    public string KeyId { get; init; } = string.Empty;

    /// <summary>
    /// The ISO's filename as it appears in the checksums document (a <c>SHA256SUMS</c> line is
    /// <c>&lt;hash&gt;  &lt;filename&gt;</c>). When empty, the last path segment of
    /// <see cref="OsCatalogEntry.IsoUrl"/> is used. This pins the hash to the right file so a checksums
    /// document listing many images can't have one entry's hash matched against another's filename.
    /// </summary>
    public string? ChecksumsFileName { get; init; }
}

/// <summary>
/// A declarative unattended-install recipe (ADR-0026): how to make an installer ISO run hands-free,
/// expressed as data so a community recipe can add it without a C# installer. Template fields support
/// placeholders filled from the answers: <c>{username}</c>, <c>{password}</c>, <c>{passwordHash}</c>,
/// <c>{hostname}</c>, <c>{locale}</c>, <c>{timezone}</c>, <c>{keyboard}</c>, and <c>{isoLabel}</c>.
/// </summary>
public sealed record UnattendedRecipe
{
    /// <summary>The seed mechanism: <c>initrd-inject</c> (a generated file injected into the installer initrd — preseed/kickstart style).</summary>
    public const string KindInitrdInject = "initrd-inject";

    /// <summary>The seed mechanism: <c>cloud-init</c> (the seed written as a NoCloud CIDATA disk — Ubuntu autoinstall style).</summary>
    public const string KindCloudInit = "cloud-init";

    /// <summary>Which mechanism drives the install (<see cref="KindInitrdInject"/> or <see cref="KindCloudInit"/>).</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>The kernel's path inside the ISO (e.g. <c>install.amd/vmlinuz</c>).</summary>
    public string KernelPath { get; init; } = string.Empty;

    /// <summary>Candidate initrd paths inside the ISO (first that exists wins, e.g. <c>install.amd/initrd.gz</c>).</summary>
    public IReadOnlyList<string> InitrdPaths { get; init; } = [];

    /// <summary>The kernel command-line template (placeholders allowed), e.g. <c>auto=true priority=critical</c>.</summary>
    public string Append { get; init; } = string.Empty;

    /// <summary>For <c>initrd-inject</c>: the seed file's name at the initramfs root (e.g. <c>preseed.cfg</c>). Unused for <c>cloud-init</c> (the file is always <c>user-data</c>).</summary>
    public string SeedFileName { get; init; } = string.Empty;

    /// <summary>The seed document template (with placeholders): a preseed/kickstart for <c>initrd-inject</c>, or the cloud-init <c>user-data</c> for <c>cloud-init</c>.</summary>
    public string SeedTemplate { get; init; } = string.Empty;
}

/// <summary>Recommended VM specs the catalog prefills for an <see cref="OsCatalogEntry"/>.</summary>
public sealed record OsRecommendedSpec
{
    /// <summary>Recommended memory in MiB.</summary>
    public int MemoryMiB { get; init; } = 2048;

    /// <summary>Recommended CPU cores.</summary>
    public int CpuCores { get; init; } = 2;

    /// <summary>Recommended primary disk size in GiB.</summary>
    public int DiskGiB { get; init; } = 20;

    /// <summary>Recommended firmware (<c>bios</c> or <c>uefi</c>).</summary>
    public string Firmware { get; init; } = "uefi";
}

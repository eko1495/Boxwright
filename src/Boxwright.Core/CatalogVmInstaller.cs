namespace Boxwright.Core;

/// <summary>
/// The resource choices for creating a VM from a catalog OS — everything the orchestration needs
/// beyond the catalog entry itself. Resource sizes typically default from
/// <see cref="OsCatalogEntry.Recommended"/>; the caller (GUI or CLI) decides whether to override them.
/// </summary>
public sealed record CatalogInstallOptions
{
    /// <summary>Human-friendly VM name.</summary>
    public required string Name { get; init; }

    /// <summary>Guest RAM in MiB.</summary>
    public int MemoryMiB { get; init; } = 2048;

    /// <summary>CPU cores.</summary>
    public int CpuCores { get; init; } = 2;

    /// <summary>Primary disk size in GiB (a cloud image is grown to this; never shrunk).</summary>
    public int DiskSizeGiB { get; init; } = 20;

    /// <summary>Firmware: <c>bios</c> or <c>uefi</c>.</summary>
    public string Firmware { get; init; } = "uefi";

    /// <summary>
    /// For an installer ISO: whether to run a hands-free unattended install (requires
    /// <see cref="Answers"/> and a supported OS family). Ignored for a cloud image, which is always
    /// seeded from <see cref="Answers"/> (the seed carries the only login the guest will have).
    /// </summary>
    public bool Unattended { get; init; }

    /// <summary>The unattended-install answers (login, hostname, …); required for a cloud image or an unattended ISO.</summary>
    public UnattendedAnswers? Answers { get; init; }
}

/// <summary>
/// Creates a VM from a catalog OS entry end-to-end: download + verify the image, create the VM folder,
/// prepare the disk, and (where applicable) generate the unattended-install seed. This is the
/// orchestration the GUI's New-VM flow performs, lifted into Core so a headless CLI runs the identical
/// path (ADR-0022). Implemented by <see cref="CatalogVmInstaller"/>.
/// </summary>
public interface ICatalogVmInstaller
{
    /// <summary>
    /// Creates and persists a VM for <paramref name="entry"/>. Reports download progress (optional) and
    /// honors cancellation; a failure rolls back the half-created VM folder so the list never shows a
    /// broken VM.
    /// </summary>
    /// <exception cref="DownloadException">The image could not be downloaded or verified.</exception>
    /// <exception cref="DiskException">A <c>qemu-img</c> operation failed.</exception>
    /// <exception cref="InstallMediaException">The installer media is unusable, or the OS family has no unattended installer.</exception>
    Task<Vm> CreateAsync(
        OsCatalogEntry entry,
        CatalogInstallOptions options,
        IProgress<IsoDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The default <see cref="ICatalogVmInstaller"/>. Mirrors the GUI's New-VM orchestration
/// (CatalogViewModel) exactly, minus the UI: an installer ISO is attached as a CD-first boot (with an
/// optional per-family unattended seed), while a cloud image is flattened into the disk, grown to the
/// requested size, and given a cloud-init login seed.
/// </summary>
public sealed class CatalogVmInstaller : ICatalogVmInstaller
{
    /// <summary>The primary disk file name written into each VM folder.</summary>
    public const string PrimaryDiskFileName = "disk.qcow2";

    private const long BytesPerGiB = 1024L * 1024 * 1024;

    private readonly IIsoDownloader _downloader;
    private readonly VmRepository _repository;
    private readonly IDiskService _diskService;
    private readonly ISeedGenerator _seedGenerator;
    private readonly IUnattendedInstallerResolver _installerResolver;

    /// <summary>Creates an installer from its Core collaborators.</summary>
    public CatalogVmInstaller(
        IIsoDownloader downloader,
        VmRepository repository,
        IDiskService diskService,
        ISeedGenerator seedGenerator,
        IUnattendedInstallerResolver installerResolver)
    {
        ArgumentNullException.ThrowIfNull(downloader);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(diskService);
        ArgumentNullException.ThrowIfNull(seedGenerator);
        ArgumentNullException.ThrowIfNull(installerResolver);
        _downloader = downloader;
        _repository = repository;
        _diskService = diskService;
        _seedGenerator = seedGenerator;
        _installerResolver = installerResolver;
    }

    /// <inheritdoc />
    public async Task<Vm> CreateAsync(
        OsCatalogEntry entry,
        CatalogInstallOptions options,
        IProgress<IsoDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Name);
        if (options.MemoryMiB <= 0 || options.CpuCores <= 0 || options.DiskSizeGiB <= 0)
        {
            throw new ArgumentException("Memory, CPU, and disk sizes must all be positive.", nameof(options));
        }

        bool isCloudImage = string.Equals(entry.ImageKind, OsCatalogEntry.ImageKindCloudImage, StringComparison.OrdinalIgnoreCase);
        if (isCloudImage && options.Answers is null)
        {
            throw new ArgumentException("A cloud image requires unattended-install answers (it carries the only login).", nameof(options));
        }

        if (options.Unattended && options.Answers is null)
        {
            throw new ArgumentException("An unattended install requires answers (login, hostname).", nameof(options));
        }

        // Re-verify a cache hit's full content at this one-time create moment (matches the GUI): catch a
        // previously-verified image that rotted on disk before it becomes a guest's install media (PR #5).
        string imagePath = await _downloader.EnsureAsync(entry, progress, reverifyCachedContent: true, cancellationToken);

        return isCloudImage
            ? await CreateFromCloudImageAsync(imagePath, options, cancellationToken)
            : await CreateFromInstallerIsoAsync(entry, imagePath, options, cancellationToken);
    }

    // Installer ISO: attach it as a CD-first boot. For an unattended install the per-family installer
    // (resolved by OS family — ADR-0016) prepares the media and returns how to boot it hands-free.
    private async Task<Vm> CreateFromInstallerIsoAsync(
        OsCatalogEntry entry, string isoPath, CatalogInstallOptions options, CancellationToken cancellationToken)
    {
        var config = new VmConfig
        {
            Name = options.Name,
            MemoryMiB = options.MemoryMiB,
            Cpu = new CpuConfig { Sockets = 1, Cores = options.CpuCores, Threads = 1 },
            Firmware = options.Firmware,
            OsType = "linux", // every catalog entry is a Linux distro today (mirrors the GUI)
            Disks = [new DiskConfig { File = PrimaryDiskFileName, Format = "qcow2", Interface = "virtio" }],
            RemovableMedia = [new RemovableMediaConfig { Type = "cdrom", File = isoPath, Attached = true }],
            Boot = new BootConfig { Order = "dc" }, // boot the installer first, then the disk
        };

        Vm vm = await _repository.CreateAsync(config, cancellationToken);
        try
        {
            if (options.Unattended)
            {
                UnattendedInstallPlan plan = _installerResolver
                    .Resolve(entry.OsFamily)
                    .Prepare(isoPath, vm.FolderPath, options.Answers!);
                VmConfig withSeed = vm.Config with
                {
                    Disks = [.. vm.Config.Disks, .. plan.SeedDisks],
                    InstallBoot = plan.Boot,
                };
                await _repository.SaveAsync(withSeed, cancellationToken);
                vm = vm with { Config = withSeed };
            }

            string diskPath = Path.Combine(vm.FolderPath, PrimaryDiskFileName);
            await _diskService.CreateAsync(diskPath, (long)options.DiskSizeGiB * BytesPerGiB, "qcow2", cancellationToken);
            return vm;
        }
        catch
        {
            await TryRollbackAsync(vm);
            throw;
        }
    }

    // Cloud image: a pre-installed qcow2. Flatten it into the VM folder (keeping the folder
    // self-contained — ADR-0006), grow it to the requested size, then attach the cloud-init login seed.
    private async Task<Vm> CreateFromCloudImageAsync(
        string imagePath, CatalogInstallOptions options, CancellationToken cancellationToken)
    {
        var config = new VmConfig
        {
            Name = options.Name,
            MemoryMiB = options.MemoryMiB,
            Cpu = new CpuConfig { Sockets = 1, Cores = options.CpuCores, Threads = 1 },
            Firmware = options.Firmware,
            OsType = "linux",
            Disks = [new DiskConfig { File = PrimaryDiskFileName, Format = "qcow2", Interface = "virtio" }],
            Boot = new BootConfig { Order = "c" }, // boot the pre-installed disk; no installer media
        };

        Vm vm = await _repository.CreateAsync(config, cancellationToken);
        try
        {
            string diskPath = Path.Combine(vm.FolderPath, PrimaryDiskFileName);
            await _diskService.CopyAsync(imagePath, diskPath, "qcow2", cancellationToken);

            // Only grow — qemu-img refuses to shrink below the image's own virtual size.
            long requestedBytes = (long)options.DiskSizeGiB * BytesPerGiB;
            DiskInfo info = await _diskService.GetInfoAsync(diskPath, cancellationToken);
            if (requestedBytes > info.VirtualSize)
            {
                await _diskService.ResizeAsync(diskPath, requestedBytes, cancellationToken);
            }

            _seedGenerator.Generate(options.Answers!, vm.FolderPath, SeedProfile.CloudImage);
            VmConfig withSeed = vm.Config with
            {
                Disks = [.. vm.Config.Disks, new DiskConfig { File = CloudInitSeedGenerator.SeedFileName, Format = "raw", Interface = "virtio" }],
            };
            await _repository.SaveAsync(withSeed, cancellationToken);
            return vm with { Config = withSeed };
        }
        catch
        {
            await TryRollbackAsync(vm);
            throw;
        }
    }

    // Best-effort cleanup of a half-created VM so a failed install never leaves a broken folder behind.
    private async Task TryRollbackAsync(Vm vm)
    {
        try
        {
            await _repository.DeleteAsync(vm.Config.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The rollback is best-effort; surface the original failure, not a cleanup error.
        }
    }
}

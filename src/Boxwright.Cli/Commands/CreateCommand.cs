using Boxwright.Core;

namespace Boxwright.Cli.Commands;

/// <summary>
/// Creates a VM. Two modes: <c>--os &lt;id&gt;</c> builds from a catalog OS (download + verify, disk
/// prep, and — for a cloud image or <c>--unattended</c> installer — a login/install seed), running the
/// same Core orchestration the GUI uses (ADR-0022). Otherwise it creates a blank VM with a fresh disk
/// and an optional bring-your-own <c>--iso</c>.
/// </summary>
internal sealed class CreateCommand : ICliCommand
{
    private const long BytesPerGiB = 1024L * 1024 * 1024;
    private const string PrimaryDiskFileName = "disk.qcow2";

    private readonly VmRepository _repository;
    private readonly IDiskService _diskService;
    private readonly IOsCatalogSource _catalog;
    private readonly ICatalogVmInstaller _installer;
    private readonly CliOutput _output;

    public CreateCommand(
        VmRepository repository,
        IDiskService diskService,
        IOsCatalogSource catalog,
        ICatalogVmInstaller installer,
        CliOutput output)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(diskService);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(installer);
        ArgumentNullException.ThrowIfNull(output);
        _repository = repository;
        _diskService = diskService;
        _catalog = catalog;
        _installer = installer;
        _output = output;
    }

    public string Name => "create";

    public string Summary => "Create a VM — from a catalog OS (--os) or blank (optionally --iso).";

    public string Usage =>
        "create <name> [--os=ID [--unattended --user=U --password=P [--hostname=H]] | --iso=PATH] " +
        "[--memory=MiB] [--cpus=N] [--disk=GiB] [--firmware=bios|uefi] [--arch=x86_64] [--os-type=linux]";

    public async Task<int> RunAsync(ParsedArgs args, CancellationToken cancellationToken)
    {
        string name = args.PositionalOrNull(0)
            ?? throw new CliException($"Usage: boxwright {Usage}");

        return args.Option("os") is { } osId
            ? await CreateFromCatalogAsync(name, osId, args, cancellationToken)
            : await CreateBlankAsync(name, args, cancellationToken);
    }

    private async Task<int> CreateFromCatalogAsync(string name, string osId, ParsedArgs args, CancellationToken cancellationToken)
    {
        IReadOnlyList<OsCatalogEntry> entries = await _catalog.GetEntriesAsync(cancellationToken);
        OsCatalogEntry entry = entries.FirstOrDefault(e => string.Equals(e.Id, osId, StringComparison.OrdinalIgnoreCase))
            ?? throw new CliException($"No catalog OS with id '{osId}'. Run 'boxwright os list' to see the ids.");

        OsRecommendedSpec recommended = entry.Recommended;
        int memory = args.IntOption("memory", recommended.MemoryMiB);
        int cpus = args.IntOption("cpus", recommended.CpuCores);
        int diskGiB = args.IntOption("disk", recommended.DiskGiB);
        string firmware = args.Option("firmware") ?? recommended.Firmware;
        if (memory <= 0 || cpus <= 0 || diskGiB <= 0)
        {
            throw new CliException("--memory, --cpus, and --disk must all be positive.");
        }

        bool isCloudImage = string.Equals(entry.ImageKind, OsCatalogEntry.ImageKindCloudImage, StringComparison.OrdinalIgnoreCase);
        bool wantsUnattended = isCloudImage || args.HasFlag("unattended");

        UnattendedAnswers? answers = wantsUnattended ? BuildAnswers(name, entry, isCloudImage, args) : null;

        var options = new CatalogInstallOptions
        {
            Name = name,
            MemoryMiB = memory,
            CpuCores = cpus,
            DiskSizeGiB = diskGiB,
            Firmware = firmware,
            // A cloud image always seeds (Core handles it); only an ISO honors the Unattended flag.
            Unattended = wantsUnattended && !isCloudImage,
            Answers = answers,
        };

        _output.Line($"Creating '{name}' from {entry.Name} {entry.Version}…");
        var progress = new ConsoleDownloadProgress(_output);
        Vm vm = await _installer.CreateAsync(entry, options, progress, cancellationToken);
        progress.Complete();

        _output.Line($"Created VM '{vm.Config.Name}' ({vm.Config.Id}).");
        _output.Line($"  Folder: {vm.FolderPath}");
        _output.Line($"  Source: {entry.Name} {entry.Version} ({(isCloudImage ? "cloud image" : "installer ISO")})");
        if (wantsUnattended)
        {
            _output.Line($"  Unattended: user '{answers!.Username}', hostname '{answers.Hostname}'.");
        }

        _output.Line($"Start it with: boxwright start {ListCommand.ShortId(vm.Config.Id)}");
        return 0;
    }

    private static UnattendedAnswers BuildAnswers(string vmName, OsCatalogEntry entry, bool isCloudImage, ParsedArgs args)
    {
        if (!isCloudImage && !entry.SupportsAutoinstall)
        {
            throw new CliException(
                $"'{entry.Id}' doesn't support unattended install. Omit --unattended to install it interactively.");
        }

        string user = args.Option("user")
            ?? throw new CliException("--user is required for an unattended install.");
        string password = args.Option("password")
            ?? throw new CliException("--password is required for an unattended install.");
        if (string.IsNullOrWhiteSpace(user))
        {
            throw new CliException("--user must not be empty.");
        }

        if (password.Length == 0)
        {
            throw new CliException("--password must not be empty.");
        }

        return new UnattendedAnswers
        {
            Username = user.Trim(),
            Password = password,
            Hostname = args.Option("hostname")?.Trim() is { Length: > 0 } h ? h : SanitizeHostname(vmName),
        };
    }

    private async Task<int> CreateBlankAsync(string name, ParsedArgs args, CancellationToken cancellationToken)
    {
        int memory = args.IntOption("memory", 2048);
        int cpus = args.IntOption("cpus", 2);
        int diskGiB = args.IntOption("disk", 20);
        string arch = args.Option("arch") ?? "x86_64";
        string firmware = args.Option("firmware") ?? "bios";
        string osType = args.Option("os-type") ?? "linux";
        string? iso = args.Option("iso");

        if (memory <= 0 || cpus <= 0 || diskGiB <= 0)
        {
            throw new CliException("--memory, --cpus, and --disk must all be positive.");
        }

        string? isoFullPath = null;
        if (iso is not null)
        {
            isoFullPath = Path.GetFullPath(iso);
            if (!File.Exists(isoFullPath))
            {
                throw new CliException($"Installer ISO not found: {isoFullPath}");
            }
        }

        var config = new VmConfig
        {
            Name = name,
            Arch = arch,
            OsType = osType,
            Firmware = firmware,
            MemoryMiB = memory,
            Cpu = new CpuConfig { Cores = cpus },
            Disks = [new DiskConfig { File = PrimaryDiskFileName, Format = "qcow2", Interface = "virtio" }],
            RemovableMedia = isoFullPath is null
                ? []
                : [new RemovableMediaConfig { Type = "cdrom", File = isoFullPath, Attached = true }],
            // Boot the installer CD first when one is attached; an empty disk falls through to it.
            Boot = new BootConfig { Order = isoFullPath is null ? "cd" : "dc" },
        };

        Vm vm = await _repository.CreateAsync(config, cancellationToken);
        string diskPath = Path.Combine(vm.FolderPath, PrimaryDiskFileName);
        await _diskService.CreateAsync(diskPath, (long)diskGiB * BytesPerGiB, "qcow2", cancellationToken);

        _output.Line($"Created VM '{vm.Config.Name}' ({vm.Config.Id}).");
        _output.Line($"  Folder: {vm.FolderPath}");
        _output.Line($"  Disk:   {diskGiB} GiB at {PrimaryDiskFileName}");
        if (isoFullPath is not null)
        {
            _output.Line($"  ISO:    {isoFullPath}");
        }

        _output.Line($"Start it with: boxwright start {ListCommand.ShortId(vm.Config.Id)}");
        return 0;
    }

    // A cloud-init/Linux hostname: lowercase, alphanumerics and hyphens only, no leading/trailing hyphen.
    private static string SanitizeHostname(string name)
    {
        char[] chars = name.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        string cleaned = new string(chars).Trim('-');
        return string.IsNullOrEmpty(cleaned) ? "boxwright" : cleaned;
    }
}

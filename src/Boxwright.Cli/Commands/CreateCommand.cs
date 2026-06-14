using Boxwright.Core;

namespace Boxwright.Cli.Commands;

/// <summary>
/// Creates a blank VM with a fresh <c>qemu-img</c> disk and an optional installer ISO. This is
/// deliberately minimal (ADR-0022): the one-click catalog download and unattended-install seed
/// generation stay in the GUI — here the user brings their own ISO via <c>--iso</c>.
/// </summary>
internal sealed class CreateCommand : ICliCommand
{
    private const long BytesPerGiB = 1024L * 1024 * 1024;
    private const string PrimaryDiskFileName = "disk.qcow2";

    private readonly VmRepository _repository;
    private readonly IDiskService _diskService;
    private readonly CliOutput _output;

    public CreateCommand(VmRepository repository, IDiskService diskService, CliOutput output)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(diskService);
        ArgumentNullException.ThrowIfNull(output);
        _repository = repository;
        _diskService = diskService;
        _output = output;
    }

    public string Name => "create";

    public string Summary => "Create a blank VM with a fresh disk (optionally with an installer ISO).";

    public string Usage =>
        "create <name> [--memory=MiB] [--cpus=N] [--disk=GiB] [--arch=x86_64] [--firmware=bios|uefi] [--os-type=linux] [--iso=PATH]";

    public async Task<int> RunAsync(ParsedArgs args, CancellationToken cancellationToken)
    {
        string name = args.PositionalOrNull(0)
            ?? throw new CliException($"Usage: boxwright {Usage}");

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
}

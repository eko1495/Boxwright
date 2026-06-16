using Boxwright.Cli.Json;
using Boxwright.Core;

namespace Boxwright.Cli.Commands;

/// <summary>Prints the full configuration of a single VM.</summary>
internal sealed class InfoCommand : ICliCommand
{
    private readonly VmResolver _resolver;
    private readonly IVmStatusProbe _statusProbe;
    private readonly IVmDiskUsageService _diskUsage;
    private readonly CliOutput _output;

    public InfoCommand(VmResolver resolver, IVmStatusProbe statusProbe, IVmDiskUsageService diskUsage, CliOutput output)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(statusProbe);
        ArgumentNullException.ThrowIfNull(diskUsage);
        ArgumentNullException.ThrowIfNull(output);
        _resolver = resolver;
        _statusProbe = statusProbe;
        _diskUsage = diskUsage;
        _output = output;
    }

    public string Name => "info";

    public string Summary => "Show a VM's configuration in detail.";

    public string Usage => "info <id|name> [--json]";

    public async Task<int> RunAsync(ParsedArgs args, CancellationToken cancellationToken)
    {
        string reference = args.PositionalOrNull(0)
            ?? throw new CliException($"Usage: boxwright {Usage}");
        Vm vm = await _resolver.ResolveAsync(reference, cancellationToken);
        VmConfig config = vm.Config;
        string status = _statusProbe.IsRunning(vm) ? "running" : "stopped";
        VmDiskUsage usage = await _diskUsage.MeasureAsync(vm, cancellationToken);

        if (args.HasFlag("json"))
        {
            _output.Line(CliJson.Write(ToJson(vm, status, usage)));
            return 0;
        }

        _output.Line($"Name:        {config.Name}");
        _output.Line($"Id:          {config.Id}");
        _output.Line($"Status:      {status}");
        _output.Line($"Folder:      {vm.FolderPath}");
        _output.Line($"OS type:     {config.OsType}");
        _output.Line($"Arch:        {config.Arch}");
        _output.Line($"Machine:     {config.Machine}");
        _output.Line($"Firmware:    {config.Firmware}");
        _output.Line($"Accelerator: {config.Accelerator}");
        _output.Line($"CPU:         {config.Cpu.Model}, {config.Cpu.Sockets}x{config.Cpu.Cores}x{config.Cpu.Threads} (sockets x cores x threads)");
        _output.Line($"Memory:      {config.MemoryMiB} MiB");
        _output.Line($"Display:     {config.Display.Protocol}{(config.Display.Gl ? " (gl)" : "")}");
        _output.Line($"Network:     {config.Network.Mode} / {config.Network.Model}");
        _output.Line($"Audio:       {(config.Audio.Enabled ? "enabled" : "disabled")}");

        if (config.Disks.Count > 0)
        {
            _output.Line("Disks:");
            for (int i = 0; i < config.Disks.Count; i++)
            {
                DiskConfig disk = config.Disks[i];
                DiskUsage? du = i < usage.Disks.Count ? usage.Disks[i] : null;
                string size = du is { Measured: true }
                    ? $", {ByteSize.Format(du.ActualBytes)} on disk / {ByteSize.Format(du.VirtualBytes)} virtual"
                    : ", size unavailable";
                _output.Line($"  - {disk.File} ({disk.Format}, {disk.Interface}{size})");
            }

            string total = usage.Disks.Any(d => d.Measured)
                ? $"{ByteSize.Format(usage.ActualBytes)} on disk / {ByteSize.Format(usage.VirtualBytes)} virtual{(usage.Complete ? "" : " (some disks unmeasured)")}"
                : "unavailable (qemu-img not found?)";
            _output.Line($"Disk usage:  {total}");
        }

        IReadOnlyList<RemovableMediaConfig> media = config.RemovableMedia;
        if (media.Count > 0)
        {
            _output.Line("Removable media:");
            foreach (RemovableMediaConfig slot in media)
            {
                string backing = slot.File is null ? "(empty)" : slot.File;
                _output.Line($"  - {slot.Type}: {backing}{(slot.Attached ? " [attached]" : "")}");
            }
        }

        return 0;
    }

    private static VmInfoJson ToJson(Vm vm, string status, VmDiskUsage usage)
    {
        VmConfig c = vm.Config;
        return new VmInfoJson(
            c.Id,
            c.Name,
            status,
            vm.FolderPath,
            c.OsType,
            c.Arch,
            c.Machine,
            c.Firmware,
            c.Accelerator,
            c.Cpu.Model,
            c.Cpu.Sockets,
            c.Cpu.Cores,
            c.Cpu.Threads,
            c.MemoryMiB,
            c.Display.Protocol,
            c.Display.Gl,
            c.Network.Mode,
            c.Network.Model,
            c.Audio.Enabled,
            c.Disks.Select((d, i) =>
            {
                DiskUsage? du = i < usage.Disks.Count ? usage.Disks[i] : null;
                bool measured = du is { Measured: true };
                return new DiskJson(d.File, d.Format, d.Interface, measured ? du!.ActualBytes : null, measured ? du!.VirtualBytes : null);
            }).ToList(),
            c.RemovableMedia.Select(m => new MediaJson(m.Type, m.File, m.Attached)).ToList(),
            usage.Disks.Any(d => d.Measured) ? usage.ActualBytes : null,
            usage.Disks.Any(d => d.Measured) ? usage.VirtualBytes : null);
    }
}

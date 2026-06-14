using Boxwright.Core;

namespace Boxwright.Cli.Commands;

/// <summary>Prints the full configuration of a single VM.</summary>
internal sealed class InfoCommand : ICliCommand
{
    private readonly VmResolver _resolver;
    private readonly IVmStatusProbe _statusProbe;
    private readonly CliOutput _output;

    public InfoCommand(VmResolver resolver, IVmStatusProbe statusProbe, CliOutput output)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(statusProbe);
        ArgumentNullException.ThrowIfNull(output);
        _resolver = resolver;
        _statusProbe = statusProbe;
        _output = output;
    }

    public string Name => "info";

    public string Summary => "Show a VM's configuration in detail.";

    public string Usage => "info <id|name>";

    public async Task<int> RunAsync(ParsedArgs args, CancellationToken cancellationToken)
    {
        string reference = args.PositionalOrNull(0)
            ?? throw new CliException($"Usage: boxwright {Usage}");
        Vm vm = await _resolver.ResolveAsync(reference, cancellationToken);
        VmConfig config = vm.Config;

        _output.Line($"Name:        {config.Name}");
        _output.Line($"Id:          {config.Id}");
        _output.Line($"Status:      {(_statusProbe.IsRunning(vm) ? "running" : "stopped")}");
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
            foreach (DiskConfig disk in config.Disks)
            {
                _output.Line($"  - {disk.File} ({disk.Format}, {disk.Interface})");
            }
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
}

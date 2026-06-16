using Boxwright.Cli.Json;
using Boxwright.Core;

namespace Boxwright.Cli.Commands;

/// <summary>
/// Manages VM templates (ADR-0025): mark a stopped VM as a reusable frozen base (<c>create</c>),
/// stamp out instances from it as linked (default) or full clones (<c>new</c>), <c>list</c> templates,
/// and <c>delete</c> one. A template can't be booted — instances are clones, which are never templates.
/// </summary>
internal sealed class TemplateCommand : ICliCommand
{
    private readonly VmResolver _resolver;
    private readonly VmRepository _repository;
    private readonly IVmCloneService _cloneService;
    private readonly IVmStatusProbe _statusProbe;
    private readonly CliOutput _output;

    public TemplateCommand(
        VmResolver resolver,
        VmRepository repository,
        IVmCloneService cloneService,
        IVmStatusProbe statusProbe,
        CliOutput output)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(cloneService);
        ArgumentNullException.ThrowIfNull(statusProbe);
        ArgumentNullException.ThrowIfNull(output);
        _resolver = resolver;
        _repository = repository;
        _cloneService = cloneService;
        _statusProbe = statusProbe;
        _output = output;
    }

    public string Name => "template";

    public string Summary => "Manage VM templates (list/create/new/delete).";

    public string Usage => "template <list [--json]|create <id|name>|new <template> <new-name> [--full]|delete <template> --yes>";

    public async Task<int> RunAsync(ParsedArgs args, CancellationToken cancellationToken)
    {
        string sub = args.PositionalOrNull(0)
            ?? throw new CliException($"Usage: boxwright {Usage}");

        return sub.ToLowerInvariant() switch
        {
            "list" => await ListAsync(args, cancellationToken),
            "create" => await CreateAsync(args, cancellationToken),
            "new" => await NewAsync(args, cancellationToken),
            "delete" => await DeleteAsync(args, cancellationToken),
            _ => throw new CliException($"Unknown 'template' subcommand '{sub}'. Usage: boxwright {Usage}"),
        };
    }

    private async Task<int> ListAsync(ParsedArgs args, CancellationToken cancellationToken)
    {
        List<Vm> templates = (await _repository.ListAsync(cancellationToken))
            .Where(v => v.Config.IsTemplate)
            .OrderBy(v => v.Config.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (args.HasFlag("json"))
        {
            VmSummaryJson[] payload = templates.Select(v => new VmSummaryJson(
                v.Config.Id, v.Config.Name, "template", v.Config.OsType, v.Config.Arch, v.Config.MemoryMiB, DiskActualBytes: null)).ToArray();
            _output.Line(CliJson.Write(payload));
            return 0;
        }

        if (templates.Count == 0)
        {
            _output.Line("No templates. Make one with 'boxwright template create <vm>'.");
            return 0;
        }

        var table = new TextTable("NAME", "ID", "OS", "ARCH", "MEMORY");
        foreach (Vm v in templates)
        {
            table.AddRow(v.Config.Name, ListCommand.ShortId(v.Config.Id), v.Config.OsType, v.Config.Arch, $"{v.Config.MemoryMiB} MiB");
        }

        _output.Out.Write(table.Render());
        return 0;
    }

    private async Task<int> CreateAsync(ParsedArgs args, CancellationToken cancellationToken)
    {
        Vm vm = await ResolveAsync(args, 1, cancellationToken);
        if (vm.Config.IsTemplate)
        {
            _output.Line($"'{vm.Config.Name}' is already a template.");
            return 0;
        }

        if (_statusProbe.IsRunning(vm))
        {
            throw new CliException($"VM '{vm.Config.Name}' is running; stop it before making it a template.");
        }

        await _repository.SaveAsync(vm.Config with { IsTemplate = true }, cancellationToken);
        _output.Line($"Marked '{vm.Config.Name}' as a template. Create instances with 'boxwright template new {ListCommand.ShortId(vm.Config.Id)} <name>'.");
        return 0;
    }

    private async Task<int> NewAsync(ParsedArgs args, CancellationToken cancellationToken)
    {
        Vm template = await ResolveAsync(args, 1, cancellationToken);
        if (!template.Config.IsTemplate)
        {
            throw new CliException($"'{template.Config.Name}' is not a template. Use 'boxwright clone' for an ordinary VM.");
        }

        string newName = args.PositionalOrNull(2)
            ?? throw new CliException($"Usage: boxwright {Usage}");

        CloneMode mode = args.HasFlag("full") ? CloneMode.Full : CloneMode.Linked;
        Vm instance = await _cloneService.CloneAsync(template, newName, mode, cancellationToken);

        string kind = mode == CloneMode.Linked ? "linked" : "full";
        _output.Line($"Created instance '{instance.Config.Name}' ({instance.Config.Id}, {kind}) from template '{template.Config.Name}'.");
        if (mode == CloneMode.Linked)
        {
            _output.Line($"  Linked: keep the template '{template.Config.Name}' in place (the instance overlays its disk).");
        }

        _output.Line($"Start it with: boxwright start {ListCommand.ShortId(instance.Config.Id)}");
        return 0;
    }

    private async Task<int> DeleteAsync(ParsedArgs args, CancellationToken cancellationToken)
    {
        Vm template = await ResolveAsync(args, 1, cancellationToken);
        if (!template.Config.IsTemplate)
        {
            throw new CliException($"'{template.Config.Name}' is not a template. Use 'boxwright delete' for an ordinary VM.");
        }

        if (!args.HasFlag("yes"))
        {
            throw new CliException(
                $"Refusing to delete template '{template.Config.Name}' without --yes. " +
                "Any linked instances created from it will break (their disks overlay this template).");
        }

        await _repository.DeleteAsync(template.Config.Id, cancellationToken);
        _output.Line($"Deleted template '{template.Config.Name}' ({template.Config.Id}).");
        return 0;
    }

    private Task<Vm> ResolveAsync(ParsedArgs args, int index, CancellationToken cancellationToken)
    {
        string reference = args.PositionalOrNull(index)
            ?? throw new CliException($"Usage: boxwright {Usage}");
        return _resolver.ResolveAsync(reference, cancellationToken);
    }
}

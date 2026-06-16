using Boxwright.Cli.Json;
using Boxwright.Core;

namespace Boxwright.Cli.Commands;

/// <summary>
/// Checks a VM's qcow2 disks for corruption via <c>qemu-img check</c> (<see cref="IVmIntegrityService"/>).
/// Requires the VM stopped — a check on a disk a running QEMU holds open reads as corrupt. Exits non-zero
/// when corruption (or an inconclusive check) is found, so it scripts as a health gate.
/// </summary>
internal sealed class CheckCommand : ICliCommand
{
    private readonly VmResolver _resolver;
    private readonly IVmStatusProbe _statusProbe;
    private readonly IVmIntegrityService _integrity;
    private readonly CliOutput _output;

    public CheckCommand(VmResolver resolver, IVmStatusProbe statusProbe, IVmIntegrityService integrity, CliOutput output)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(statusProbe);
        ArgumentNullException.ThrowIfNull(integrity);
        ArgumentNullException.ThrowIfNull(output);
        _resolver = resolver;
        _statusProbe = statusProbe;
        _integrity = integrity;
        _output = output;
    }

    public string Name => "check";

    public string Summary => "Check a VM's disks for corruption (qemu-img check).";

    public string Usage => "check <id|name> [--repair] [--json]";

    public async Task<int> RunAsync(ParsedArgs args, CancellationToken cancellationToken)
    {
        string reference = args.PositionalOrNull(0)
            ?? throw new CliException($"Usage: boxwright {Usage}");
        Vm vm = await _resolver.ResolveAsync(reference, cancellationToken);

        if (_statusProbe.IsRunning(vm))
        {
            throw new CliException(
                $"VM '{vm.Config.Name}' is running; stop it first (a check on a live disk reports false corruption" +
                ", and repairing it would race the guest).");
        }

        // --repair opts into qemu-img check -r all, which rewrites the image and may discard unrecoverable data.
        DiskRepairMode repair = args.HasFlag("repair") ? DiskRepairMode.All : DiskRepairMode.None;
        VmIntegrityReport report = await _integrity.CheckAsync(vm, repair, cancellationToken);

        if (args.HasFlag("json"))
        {
            _output.Line(CliJson.Write(ToJson(report)));
        }
        else
        {
            Print(vm, report, repair);
        }

        // 0 when every checkable disk is consistent; non-zero on corruption, a failed check, or nothing to check.
        return report.Healthy ? 0 : 1;
    }

    private void Print(Vm vm, VmIntegrityReport report, DiskRepairMode repair)
    {
        if (!report.Checked)
        {
            _output.Line($"VM '{vm.Config.Name}' has no qcow2 disks to check.");
            return;
        }

        foreach (DiskIntegrity disk in report.Disks)
        {
            if (disk.Error is not null)
            {
                _output.Line($"  {disk.File}: check failed — {disk.Error}");
            }
            else if (disk.Result is { } r)
            {
                string verdict = r.Healthy ? "OK" : "CORRUPTED";
                string fixedNote = repair != DiskRepairMode.None && r.Repaired
                    ? $", fixed {r.CorruptionsFixed} corruptions / {r.LeaksFixed} leaks"
                    : string.Empty;
                _output.Line($"  {disk.File}: {verdict} ({r.Corruptions} corruptions, {r.Leaks} leaks{fixedNote})");
            }
        }

        if (repair != DiskRepairMode.None)
        {
            _output.Line(report.Healthy
                ? $"'{vm.Config.Name}': repaired — all disks now consistent."
                : $"'{vm.Config.Name}': some problems could not be repaired — see above.");
        }
        else
        {
            _output.Line(report.Healthy
                ? $"'{vm.Config.Name}': all disks consistent."
                : $"'{vm.Config.Name}': problems found — run with --repair to attempt a fix.");
        }
    }

    private static IntegrityJson ToJson(VmIntegrityReport report) => new(
        report.Healthy,
        report.Checked,
        report.Disks.Select(d => new DiskCheckJson(
            d.File,
            d.Result?.Healthy,
            d.Result?.Corruptions,
            d.Result?.Leaks,
            d.Error)).ToArray());
}

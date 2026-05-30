using Boxwright.Core;

namespace Boxwright.App.ViewModels;

/// <summary>
/// A single row in the VM list: a thin, display-focused wrapper over a
/// <see cref="Vm"/>. Read-only for APP-2; runtime status and actions arrive later.
/// </summary>
public sealed class VmListItemViewModel
{
    public VmListItemViewModel(Vm vm)
    {
        ArgumentNullException.ThrowIfNull(vm);
        Vm = vm;
    }

    /// <summary>The underlying domain VM.</summary>
    public Vm Vm { get; }

    /// <summary>Display name, with a fallback when the config has no name.</summary>
    public string Name =>
        string.IsNullOrWhiteSpace(Vm.Config.Name) ? "(unnamed VM)" : Vm.Config.Name;

    /// <summary>One-line spec summary, e.g. <c>x86_64 · 2 vCPU · 2048 MiB</c>.</summary>
    public string Summary
    {
        get
        {
            CpuConfig cpu = Vm.Config.Cpu;
            int vcpus = cpu.Sockets * cpu.Cores * cpu.Threads;
            return $"{Vm.Config.Arch} · {vcpus} vCPU · {Vm.Config.MemoryMiB} MiB";
        }
    }
}

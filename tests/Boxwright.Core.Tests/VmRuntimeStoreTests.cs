using Boxwright.Qmp;
using Xunit;

namespace Boxwright.Core.Tests;

public sealed class VmRuntimeStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bw-runtime-" + Guid.NewGuid().ToString("N"));

    public VmRuntimeStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private Vm NewVm() => new(_dir, new VmConfig { Name = "Test" });

    [Fact]
    public void SaveThenLoad_TcpEndpoint_RoundTrips()
    {
        var store = new VmRuntimeStore();
        Vm vm = NewVm();
        store.Save(vm, VmRuntimeState.From(4321, QmpEndpoint.Tcp("127.0.0.1", 4444), 5930, "spice", 5931, Accelerator.Whpx));

        VmRuntimeState? loaded = store.TryLoad(vm);

        Assert.NotNull(loaded);
        Assert.Equal(4321, loaded!.ProcessId);
        Assert.Equal(5930, loaded.SpicePort);
        Assert.Equal("spice", loaded.DisplayProtocol);
        Assert.Equal(5931, loaded.GuestAgentPort);
        Assert.Equal(Accelerator.Whpx, loaded.Accelerator);

        QmpEndpoint endpoint = loaded.ToQmpEndpoint();
        Assert.Equal(QmpTransport.Tcp, endpoint.Transport);
        Assert.Equal("127.0.0.1", endpoint.Host);
        Assert.Equal(4444, endpoint.Port);
    }

    [Fact]
    public void SaveThenLoad_UnixEndpoint_RoundTrips()
    {
        var store = new VmRuntimeStore();
        Vm vm = NewVm();
        store.Save(vm, VmRuntimeState.From(7, QmpEndpoint.UnixSocket("/tmp/qmp.sock"), 5901, "vnc", 5902, Accelerator.Kvm));

        QmpEndpoint endpoint = store.TryLoad(vm)!.ToQmpEndpoint();

        Assert.Equal(QmpTransport.Unix, endpoint.Transport);
        Assert.Equal("/tmp/qmp.sock", endpoint.SocketPath);
    }

    [Fact]
    public void TryLoad_NoFile_ReturnsNull() => Assert.Null(new VmRuntimeStore().TryLoad(NewVm()));

    [Fact]
    public void Clear_RemovesTheRuntimeFile()
    {
        var store = new VmRuntimeStore();
        Vm vm = NewVm();
        store.Save(vm, VmRuntimeState.From(1, QmpEndpoint.Tcp("127.0.0.1", 4444), 5930, "spice", 5931, Accelerator.Tcg));
        Assert.NotNull(store.TryLoad(vm));

        store.Clear(vm);

        Assert.Null(store.TryLoad(vm));
    }
}

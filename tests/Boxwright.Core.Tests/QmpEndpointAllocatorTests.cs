using Boxwright.Qmp;
using Xunit;

namespace Boxwright.Core.Tests;

// CORE-8: per-launch endpoint allocation.
public class QmpEndpointAllocatorTests
{
    [Fact]
    public void AllocateFreeTcpPort_ReturnsPortInRange()
    {
        var allocator = new QmpEndpointAllocator();

        int port = allocator.AllocateFreeTcpPort();

        Assert.InRange(port, 1, 65535);
    }

    [Fact]
    public void AllocateQmpEndpoint_MatchesHostTransport()
    {
        var allocator = new QmpEndpointAllocator();

        QmpEndpoint endpoint = allocator.AllocateQmpEndpoint();

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(QmpTransport.Tcp, endpoint.Transport);
            Assert.InRange(endpoint.Port, 1, 65535);
        }
        else
        {
            Assert.Equal(QmpTransport.Unix, endpoint.Transport);
            Assert.False(string.IsNullOrEmpty(endpoint.SocketPath));
        }
    }
}

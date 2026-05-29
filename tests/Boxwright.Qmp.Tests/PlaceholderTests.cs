using Xunit;

namespace Boxwright.Qmp.Tests;

// Placeholder so the BLD-1 build/test baseline is green. Replaced by real tests
// as Boxwright.Qmp gains behavior — fake loopback QMP server + handshake/execute
// coverage (see docs/backlog.md, QMP-2 onward).
public class PlaceholderTests
{
    [Fact]
    public void TestInfrastructure_IsWired() => Assert.True(true);
}

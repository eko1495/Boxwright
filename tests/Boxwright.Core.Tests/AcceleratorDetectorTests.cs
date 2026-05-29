using Xunit;

namespace Boxwright.Core.Tests;

// CORE-4: accelerator selection logic, tested via a fake probe (no dependence on
// the dev machine's real capabilities).
public class AcceleratorDetectorTests
{
    [Fact]
    public void Detect_PrefersKvm_WhenAvailable()
    {
        var detector = new AcceleratorDetector(new FakeProbe { Kvm = true, Hvf = true, Whpx = true });

        Assert.Equal(Accelerator.Kvm, detector.Detect());
    }

    [Fact]
    public void Detect_PicksHvf_WhenOnlyHvfAvailable()
    {
        var detector = new AcceleratorDetector(new FakeProbe { Hvf = true });

        Assert.Equal(Accelerator.Hvf, detector.Detect());
    }

    [Fact]
    public void Detect_PicksWhpx_WhenOnlyWhpxAvailable()
    {
        var detector = new AcceleratorDetector(new FakeProbe { Whpx = true });

        Assert.Equal(Accelerator.Whpx, detector.Detect());
    }

    [Fact]
    public void Detect_FallsBackToTcg_WhenNoHardwareAccel()
    {
        var detector = new AcceleratorDetector(new FakeProbe());

        Assert.Equal(Accelerator.Tcg, detector.Detect());
    }

    [Theory]
    [InlineData(Accelerator.Kvm, "kvm")]
    [InlineData(Accelerator.Hvf, "hvf")]
    [InlineData(Accelerator.Whpx, "whpx")]
    [InlineData(Accelerator.Tcg, "tcg")]
    public void ToQemuValue_MapsToAccelString(Accelerator accelerator, string expected)
    {
        Assert.Equal(expected, accelerator.ToQemuValue());
    }

    [Fact]
    public void DefaultProbe_DoesNotReportForeignAccelerators()
    {
        var probe = new DefaultHostAccelerationProbe();

        // Whatever the host OS is, the other platforms' accelerators must report false.
        if (OperatingSystem.IsWindows())
        {
            Assert.False(probe.IsKvmAvailable());
            Assert.False(probe.IsHvfAvailable());
        }
        else if (OperatingSystem.IsLinux())
        {
            Assert.False(probe.IsHvfAvailable());
            Assert.False(probe.IsWhpxAvailable());
        }
        else if (OperatingSystem.IsMacOS())
        {
            Assert.False(probe.IsKvmAvailable());
            Assert.False(probe.IsWhpxAvailable());
        }
    }

    private sealed class FakeProbe : IHostAccelerationProbe
    {
        public bool Kvm { get; init; }

        public bool Hvf { get; init; }

        public bool Whpx { get; init; }

        public bool IsKvmAvailable() => Kvm;

        public bool IsHvfAvailable() => Hvf;

        public bool IsWhpxAvailable() => Whpx;
    }
}

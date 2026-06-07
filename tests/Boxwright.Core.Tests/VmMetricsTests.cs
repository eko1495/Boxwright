using Boxwright.Core;
using Xunit;

namespace Boxwright.Core.Tests;

public sealed class VmMetricsTests
{
    [Fact]
    public void Derive_ComputesCpuPercentMemoryAndDiskRate()
    {
        var previous = new VmMetricsSample(TimeSpan.FromSeconds(1), 100_000_000, 0, 0);
        var current = new VmMetricsSample(TimeSpan.FromSeconds(1.5), 200_000_000, 1_000_000, 1_000_000);

        VmMetricsRate rate = VmMetrics.Derive(previous, current, wallSeconds: 1.0, vCpuCount: 2);

        Assert.Equal(25.0, rate.CpuPercent, 3);            // 0.5 s CPU / 1 s wall / 2 vCPUs
        Assert.Equal(200.0, rate.MemoryMegabytes, 3);      // current working set
        Assert.Equal(2.0, rate.DiskMegabytesPerSecond, 3); // (1 + 1) MB over 1 s
    }

    [Fact]
    public void Derive_ClampsCpuToHundredPercent()
    {
        var previous = new VmMetricsSample(TimeSpan.Zero, 0, 0, 0);
        var current = new VmMetricsSample(TimeSpan.FromSeconds(10), 0, 0, 0); // absurd CPU delta

        Assert.Equal(100.0, VmMetrics.Derive(previous, current, wallSeconds: 1.0, vCpuCount: 1).CpuPercent, 3);
    }

    [Fact]
    public void Derive_ZeroWall_YieldsZeroRates_NotNaN()
    {
        var sample = new VmMetricsSample(TimeSpan.FromSeconds(1), 50_000_000, 5, 5);

        VmMetricsRate rate = VmMetrics.Derive(sample, sample, wallSeconds: 0, vCpuCount: 4);

        Assert.Equal(0, rate.CpuPercent);
        Assert.Equal(0, rate.DiskMegabytesPerSecond);
        Assert.Equal(50.0, rate.MemoryMegabytes, 3); // RAM is instantaneous, still reported
    }

    [Fact]
    public void Derive_NeverDividesByZeroVcpus()
    {
        var previous = new VmMetricsSample(TimeSpan.Zero, 0, 0, 0);
        var current = new VmMetricsSample(TimeSpan.FromSeconds(0.5), 0, 0, 0);

        // vCpuCount 0 is treated as 1 (no divide-by-zero).
        Assert.Equal(50.0, VmMetrics.Derive(previous, current, wallSeconds: 1.0, vCpuCount: 0).CpuPercent, 3);
    }
}

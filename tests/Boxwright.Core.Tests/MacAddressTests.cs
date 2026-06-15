using Xunit;

namespace Boxwright.Core.Tests;

public sealed class MacAddressTests
{
    [Fact]
    public void Generate_UsesQemuPrefix_AndIsValid()
    {
        string mac = MacAddress.Generate();

        Assert.StartsWith("52:54:00:", mac, StringComparison.Ordinal);
        Assert.True(MacAddress.IsValid(mac));
    }

    [Fact]
    public void Generate_ProducesUniqueAddresses()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < 200; i++)
        {
            seen.Add(MacAddress.Generate());
        }

        // 200 draws from 2^24 — a collision would be a real RNG bug, not chance.
        Assert.Equal(200, seen.Count);
    }

    [Theory]
    [InlineData("52:54:00:ab:cd:ef", true)]
    [InlineData("00:11:22:33:44:55", true)]
    [InlineData("52:54:00:AB:CD:EF", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("52:54:00:ab:cd", false)]      // too few octets
    [InlineData("52:54:00:ab:cd:ef:00", false)] // too many
    [InlineData("52-54-00-ab-cd-ef", false)]   // wrong separator
    [InlineData("52:54:00:ab:cd:gg", false)]   // non-hex
    public void IsValid_ChecksSixHexOctets(string? mac, bool expected) =>
        Assert.Equal(expected, MacAddress.IsValid(mac));
}

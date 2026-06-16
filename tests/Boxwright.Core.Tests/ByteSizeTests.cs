using Boxwright.Core;
using Xunit;

namespace Boxwright.Core.Tests;

public sealed class ByteSizeTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1 KiB")]
    [InlineData(1536, "2 KiB")] // whole KiB (rounded)
    [InlineData(1048576, "1.0 MiB")]
    [InlineData(1572864, "1.5 MiB")]
    [InlineData(1073741824, "1.0 GiB")]
    [InlineData(3865470566, "3.6 GiB")]
    [InlineData(1099511627776, "1.0 TiB")]
    public void Format_uses_binary_units(long bytes, string expected)
    {
        Assert.Equal(expected, ByteSize.Format(bytes));
    }
}

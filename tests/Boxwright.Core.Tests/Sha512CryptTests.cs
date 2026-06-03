using Boxwright.Core;
using Xunit;

namespace Boxwright.Core.Tests;

public sealed class Sha512CryptTests
{
    [Theory]
    // The canonical published $6$ (SHA-512 crypt) vector from Drepper's specification.
    [InlineData(
        "Hello world!",
        "saltstring",
        "$6$saltstring$svn8UoSVapNtMuq1ukKS4tPQd8iKwSMHWjl/O817G3uBnIFNjnQJuesI68u4OTLiBFdcbYEdFCoEOfaS35inz1")]
    public void Hash_MatchesKnownVector(string password, string salt, string expected)
    {
        Assert.Equal(expected, Sha512Crypt.Hash(password, salt));
    }

    [Fact]
    public void Hash_CapsSaltAt16Characters()
    {
        // A salt longer than 16 chars hashes identically to its 16-char prefix.
        string full = Sha512Crypt.Hash("pw", "0123456789abcdefGHIJ");
        string prefix = Sha512Crypt.Hash("pw", "0123456789abcdef");

        Assert.Equal(prefix, full);
        Assert.StartsWith("$6$0123456789abcdef$", full);
    }

    [Fact]
    public void Hash_RandomSalt_ProducesWellFormedSixDollarHash()
    {
        string hash = Sha512Crypt.Hash("correct horse battery staple");

        string[] parts = hash.Split('$');
        Assert.Equal(4, parts.Length);     // "", "6", salt, digest
        Assert.Equal("6", parts[1]);
        Assert.Equal(16, parts[2].Length); // 16-char salt
        Assert.Equal(86, parts[3].Length); // SHA-512 crypt digest length
    }

    [Fact]
    public void Hash_RandomSalt_DiffersBetweenCalls()
    {
        Assert.NotEqual(Sha512Crypt.Hash("same"), Sha512Crypt.Hash("same"));
    }
}

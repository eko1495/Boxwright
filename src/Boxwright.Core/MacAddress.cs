using System.Security.Cryptography;

namespace Boxwright.Core;

/// <summary>
/// Generates and validates VM NIC MAC addresses. New addresses use QEMU's registered OUI prefix
/// <c>52:54:00</c> (a locally-administered, unicast range) with three random octets, so each VM gets a
/// unique MAC instead of QEMU's shared default — which otherwise collides for two VMs on the same bridge
/// (ADR-0024/0025).
/// </summary>
public static class MacAddress
{
    /// <summary>The QEMU OUI prefix used for generated addresses.</summary>
    public const string Prefix = "52:54:00";

    /// <summary>Generates a random <c>52:54:00:xx:xx:xx</c> MAC.</summary>
    public static string Generate()
    {
        byte[] tail = RandomNumberGenerator.GetBytes(3);
        return $"{Prefix}:{tail[0]:x2}:{tail[1]:x2}:{tail[2]:x2}";
    }

    /// <summary>True if <paramref name="mac"/> is six colon-separated hex octets (e.g. <c>52:54:00:ab:cd:ef</c>).</summary>
    public static bool IsValid(string? mac)
    {
        if (string.IsNullOrEmpty(mac))
        {
            return false;
        }

        string[] octets = mac.Split(':');
        return octets.Length == 6 && octets.All(o => o.Length == 2 && o.All(Uri.IsHexDigit));
    }
}

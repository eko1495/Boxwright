using System.Globalization;

namespace Boxwright.Core;

/// <summary>Formats byte counts for human-readable output using binary units (KiB/MiB/GiB/TiB).</summary>
public static class ByteSize
{
    private static readonly string[] Units = ["B", "KiB", "MiB", "GiB", "TiB", "PiB"];

    /// <summary>
    /// Formats <paramref name="bytes"/> as e.g. <c>3.4 GiB</c> (one decimal place above KiB; whole bytes/KiB).
    /// </summary>
    public static string Format(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        // KiB as a whole number; MiB and up with one decimal.
        string number = unit <= 1
            ? value.ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.0", CultureInfo.InvariantCulture);
        return $"{number} {Units[unit]}";
    }
}

using System;
using System.Globalization;

namespace Axiom.Mesh.Cli;

/// <summary>How the command line writes values in text: ids shortened, times in UTC, sizes in binary units. Pure.</summary>
internal static class Formats
{
    /// <summary>The hex digits of an id a table shows; every command accepts such a prefix back.</summary>
    public const int ShortId = 12;

    /// <summary>An id shortened for a table.</summary>
    /// <param name="id">The id.</param>
    /// <returns>Its first 12 hex digits; <c>-</c> for an all-zero id.</returns>
    public static string Short(Hash32 id) => id.IsZero ? "-" : id.ToHex()[..ShortId];

    /// <summary>A time of LOCAL-SPEC.md §3.2 (milliseconds since the Unix epoch) in UTC.</summary>
    /// <param name="milliseconds">The time.</param>
    /// <returns><c>2026-10-29 12:00:00Z</c>; <c>-</c> for 0, which means never.</returns>
    public static string Time(ulong milliseconds) => milliseconds == 0
        ? "-"
        : DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Min(milliseconds, (ulong)DateTimeOffset.MaxValue.ToUnixTimeMilliseconds()))
            .UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>A size in bytes, in binary units with one decimal above 1 KiB.</summary>
    /// <param name="bytes">The size.</param>
    /// <returns><c>512 B</c>, <c>1.5 KiB</c>, <c>3.0 MiB</c>.</returns>
    public static string Bytes(ulong bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? bytes.ToString(CultureInfo.InvariantCulture) + " B"
            : value.ToString("0.0", CultureInfo.InvariantCulture) + " " + units[unit];
    }

    /// <summary>A yes/no column.</summary>
    /// <param name="value">The value.</param>
    /// <returns><c>yes</c> or <c>no</c>.</returns>
    public static string YesNo(bool value) => value ? "yes" : "no";

    /// <summary>An enumeration value as a lower-case word.</summary>
    /// <typeparam name="T">The enumeration.</typeparam>
    /// <param name="value">The value.</param>
    /// <returns>Its name in lower case; its number when it has no name.</returns>
    public static string Word<T>(T value)
        where T : struct, Enum => Enum.IsDefined(value) ? value.ToString().ToLowerInvariant() : Convert.ToUInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
}

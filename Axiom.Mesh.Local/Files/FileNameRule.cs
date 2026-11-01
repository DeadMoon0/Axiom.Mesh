using System;
using System.Collections.Frozen;

namespace Axiom.Mesh.Local;

/// <summary>
/// SPEC.md §8.4 and §11.4 on the file system a Tree is written to: a Tree name (already free of <c>/</c>, <c>\</c>, NUL,
/// <c>.</c> and <c>..</c>) is written only when the platform can hold it as one file name and it does not start with
/// <see cref="ReservedPrefix"/>, which the helpers and the services keep for their own markers and temporary files; on
/// Windows a name with a character Windows refuses (a colon would open a data stream), a control character, a trailing dot
/// or space, or a reserved device name is a conflict. A folder scanned for publishing leaves every reserved name out. Pure.
/// </summary>
public static class FileNameRule
{
    /// <summary>
    /// The prefix of every name the helpers and services keep for themselves: <see cref="StoreFiles.MarkerName"/>,
    /// <see cref="StoreFiles.PartialPrefix"/>, the sync service's marker. Such a name is refused on write and ignored on scan.
    /// </summary>
    public const string ReservedPrefix = ".axiom-mesh-";

    private static readonly FrozenSet<string> Reserved = new[]
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private const string RefusedOnWindows = @"<>:""|?*\/";

    /// <summary>Whether a name is one the helpers and services keep for themselves (<see cref="ReservedPrefix"/>).</summary>
    /// <param name="name">A file or directory name.</param>
    /// <returns><see langword="true"/> for a reserved name.</returns>
    public static bool IsReserved(string name) => name is not null && name.StartsWith(ReservedPrefix, StringComparison.Ordinal);

    /// <summary>Whether a Tree name may be written as one file name on a platform.</summary>
    /// <param name="name">The name.</param>
    /// <param name="windows">The platform is Windows.</param>
    /// <returns><see langword="false"/> for a conflict.</returns>
    public static bool MayWrite(string name, bool windows)
    {
        if (string.IsNullOrEmpty(name) || name is "." or ".." || name.Contains('/', StringComparison.Ordinal) || name.Contains('\0', StringComparison.Ordinal))
            return false;
        if (IsReserved(name))
            return false;
        if (!windows)
            return true;
        if (name.AsSpan().IndexOfAny(RefusedOnWindows) >= 0 || HasControlCharacter(name) || name.EndsWith('.') || name.EndsWith(' '))
            return false;
        string stem = name.Split('.')[0];
        return !Reserved.Contains(stem);
    }

    private static bool HasControlCharacter(string name)
    {
        foreach (char c in name)
        {
            if (c < ' ')
                return true;
        }

        return false;
    }
}

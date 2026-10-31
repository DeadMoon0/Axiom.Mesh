using System;
using System.Collections.Frozen;

namespace Axiom.Mesh.Local;

/// <summary>
/// SPEC.md §8.4 and §11.4 on the file system a Tree is written to: a Tree name (already free of <c>/</c>, <c>\</c>, NUL,
/// <c>.</c> and <c>..</c>) is written only when the platform can hold it as one file name and it is none of the names
/// the helpers keep for themselves; on Windows a name with a character Windows refuses (a colon would open a data
/// stream), a control character, a trailing dot or space, or a reserved device name is a conflict. Pure.
/// </summary>
internal static class FileNameRule
{
    private static readonly FrozenSet<string> Reserved = new[]
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private const string RefusedOnWindows = "<>:\"|?*\\/";

    /// <summary>Whether a Tree name may be written as one file name on a platform.</summary>
    /// <param name="name">The name.</param>
    /// <param name="windows">The platform is Windows.</param>
    /// <returns><see langword="false"/> for a conflict.</returns>
    public static bool MayWrite(string name, bool windows)
    {
        if (string.IsNullOrEmpty(name) || name is "." or ".." || name.Contains('/', StringComparison.Ordinal) || name.Contains('\0', StringComparison.Ordinal))
            return false;
        if (name.StartsWith(StoreFiles.MarkerName, StringComparison.Ordinal) || name.StartsWith(StoreFiles.PartialPrefix, StringComparison.Ordinal))
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

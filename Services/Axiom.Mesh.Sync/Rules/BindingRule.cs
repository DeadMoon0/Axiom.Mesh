using System;
using System.Collections.Generic;

namespace Axiom.Mesh.Sync;

/// <summary>
/// Which directories may be bound (SYNC-SERVICE.md §2): one directory is kept in sync with one Archive, with all below it; a
/// directory bound already, one inside a bound directory and one holding a bound directory are refused, so no file belongs to
/// two bindings. Paths are compared as given (absolute, normalised by the caller), with <c>/</c> and <c>\</c> alike, ignoring
/// case where the file system does. Pure.
/// </summary>
internal static class BindingRule
{
    /// <summary>Whether a directory may be bound beside the bound ones.</summary>
    /// <param name="bound">The bound directories.</param>
    /// <param name="candidate">The directory to bind.</param>
    /// <param name="caseInsensitive">Whether the file system ignores case (Windows).</param>
    /// <returns>The verdict.</returns>
    public static BindingVerdict Check(IReadOnlyList<string> bound, string candidate, bool caseInsensitive)
    {
        ArgumentNullException.ThrowIfNull(bound);
        ArgumentException.ThrowIfNullOrEmpty(candidate);
        StringComparison comparison = caseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string wanted = Normal(candidate);
        foreach (string directory in bound)
        {
            string existing = Normal(directory);
            if (string.Equals(existing, wanted, comparison))
                return BindingVerdict.AlreadyBound;
            if (wanted.StartsWith(existing + "/", comparison) || existing.StartsWith(wanted + "/", comparison))
                return BindingVerdict.Nested;
        }

        return BindingVerdict.Allowed;
    }

    // One separator, no trailing one (a root such as "/" keeps it).
    private static string Normal(string path)
    {
        string forward = path.Replace('\\', '/');
        return forward.Length > 1 ? forward.TrimEnd('/') : forward;
    }
}

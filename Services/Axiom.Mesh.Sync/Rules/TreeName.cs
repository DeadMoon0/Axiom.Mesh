using System.Text;

namespace Axiom.Mesh.Sync;

/// <summary>
/// Whether a file or directory name of a bound directory can be published as a Tree entry name (SPEC.md §8.4, §5.2): not empty,
/// not <c>.</c> or <c>..</c>, no <c>/</c>, <c>\</c> or NUL, at most 255 UTF-8 bytes, well-formed UTF-16. A name that cannot is a
/// <see cref="ConflictKind.NameRefused"/> conflict and stays as it is. Pure.
/// </summary>
internal static class TreeName
{
    /// <summary>Whether a Tree may hold the name.</summary>
    /// <param name="name">The name.</param>
    /// <returns><see langword="true"/> when it may.</returns>
    public static bool MayHold(string name)
    {
        if (string.IsNullOrEmpty(name) || name is "." or ".." || name.IndexOfAny(['/', '\\', '\0']) >= 0)
            return false;
        for (int i = 0; i < name.Length; i++)
        {
            if (char.IsHighSurrogate(name[i]) && i + 1 < name.Length && char.IsLowSurrogate(name[i + 1]))
                i++;
            else if (char.IsSurrogate(name[i]))
                return false;
        }

        return Encoding.UTF8.GetByteCount(name) <= Limits.MaxTreeNameLength;
    }
}

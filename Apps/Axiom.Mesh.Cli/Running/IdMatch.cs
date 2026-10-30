using System;
using System.Collections.Generic;
using System.Linq;

namespace Axiom.Mesh.Cli;

/// <summary>
/// How a command line names an id (fundamentals plan §2.7): the full 64 hex digits, an unambiguous prefix of them as a
/// table shows it, or, for what has one, its exact name. Pure: the candidates are read by the caller.
/// </summary>
internal static class IdMatch
{
    /// <summary>The most candidates an ambiguity line names.</summary>
    public const int MaxNamed = 5;

    /// <summary>Whether a text is a full id: 64 hex digits.</summary>
    /// <param name="text">The text.</param>
    /// <param name="id">The id, or zero.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public static bool IsFull(string text, out Hash32 id)
    {
        ArgumentNullException.ThrowIfNull(text);
        id = default;
        return text.Length == Hash32.Length * 2 && Hash32.TryParseHex(text.ToLowerInvariant(), out id);
    }

    /// <summary>Finds the one candidate a text names.</summary>
    /// <typeparam name="T">The candidates' type.</typeparam>
    /// <param name="text">The text: hex digits or a name.</param>
    /// <param name="candidates">Everything it may name.</param>
    /// <param name="id">A candidate's id.</param>
    /// <param name="name">A candidate's name; empty when it has none.</param>
    /// <param name="what">What the candidates are, for the problem line: <c>package</c>.</param>
    /// <param name="match">The one candidate named; or the default.</param>
    /// <param name="problem">Why none is named, in one line; <see langword="null"/> on success.</param>
    /// <param name="ambiguous">Whether the problem is that several are named.</param>
    /// <returns><see langword="true"/> when exactly one candidate is named.</returns>
    public static bool TryFind<T>(string text, IReadOnlyList<T> candidates, Func<T, Hash32> id, Func<T, string> name, string what, out T? match, out string? problem, out bool ambiguous)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(name);
        string prefix = text.ToLowerInvariant();
        bool hex = prefix.Length > 0 && prefix.All(char.IsAsciiHexDigit);
        List<T> found = hex ? [.. candidates.Where(c => id(c).ToHex().StartsWith(prefix, StringComparison.Ordinal))] : [];
        if (found.Count == 0)
            found = [.. candidates.Where(c => string.Equals(name(c), text, StringComparison.Ordinal))];

        match = found.Count == 1 ? found[0] : default;
        ambiguous = found.Count > 1;
        problem = found.Count switch
        {
            1 => null,
            0 => $"no {what} matches '{text}'",
            _ => $"'{text}' matches {found.Count} {what}s: "
                + string.Join(", ", found.Take(MaxNamed).Select(c => name(c).Length == 0 ? Formats.Short(id(c)) : $"{Formats.Short(id(c))} ({name(c)})"))
                + (found.Count > MaxNamed ? ", ..." : string.Empty)
                + "; give more of the id",
        };
        return found.Count == 1;
    }
}

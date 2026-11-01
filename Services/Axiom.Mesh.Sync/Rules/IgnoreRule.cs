using System;
using System.Collections.Generic;
using System.Linq;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Sync;

/// <summary>
/// The names a binding leaves alone (SYNC-SERVICE.md §2): exact names (<c>node_modules</c>, <c>Thumbs.db</c>) and extensions
/// (<c>*.tmp</c>), matched against every name of a path, case-insensitively; a matched directory is left alone with everything in
/// it. Names starting with <see cref="FileNameRule.ReservedPrefix"/> are always left alone: the service's marker, the helpers'
/// temporary files. An ignored path is neither published nor written, and the branch keeps the state it had. Pure.
/// </summary>
internal sealed class IgnoreRule
{
    private readonly HashSet<string> names;
    private readonly string[] extensions;

    private IgnoreRule(HashSet<string> names, string[] extensions, IReadOnlyList<string> patterns)
    {
        this.names = names;
        this.extensions = extensions;
        Patterns = patterns;
    }

    /// <summary>The rule that leaves only the reserved names alone.</summary>
    public static IgnoreRule None { get; } = new([], [], []);

    /// <summary>The patterns, as given.</summary>
    public IReadOnlyList<string> Patterns { get; }

    /// <summary>Reads patterns.</summary>
    /// <param name="patterns">Exact names and <c>*.ext</c>; at most <see cref="SyncLimits.IgnorePatterns"/>.</param>
    /// <param name="rule">The rule, or <see langword="null"/>.</param>
    /// <param name="problem">Why a pattern is not one, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when every pattern is one.</returns>
    public static bool TryParse(IReadOnlyList<string> patterns, out IgnoreRule? rule, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        rule = null;
        if (patterns.Count > SyncLimits.IgnorePatterns)
        {
            problem = $"{patterns.Count} ignore patterns; a binding takes at most {SyncLimits.IgnorePatterns}.";
            return false;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var extensions = new List<string>();
        foreach (string pattern in patterns)
        {
            if (string.IsNullOrEmpty(pattern) || pattern.Contains('/', StringComparison.Ordinal) || pattern.Contains('\\', StringComparison.Ordinal) || pattern is "." or "..")
            {
                problem = $"'{pattern}' is no ignore pattern: give a name or *.ext, without a path.";
                return false;
            }

            int star = pattern.IndexOf('*', StringComparison.Ordinal);
            if (star < 0)
            {
                names.Add(pattern);
            }
            else if (star == 0 && pattern.Length > 2 && pattern[1] == '.' && pattern.IndexOf('*', 1) < 0)
            {
                extensions.Add(pattern[1..]);
            }
            else
            {
                problem = $"'{pattern}' is no ignore pattern: a star stands only first, as in *.tmp.";
                return false;
            }
        }

        rule = new IgnoreRule(names, [.. extensions], [.. patterns]);
        problem = null;
        return true;
    }

    /// <summary>Whether a path is left alone: one of its names is reserved, ignored by name, or has an ignored extension.</summary>
    /// <param name="path">The path relative to the bound directory, with <c>/</c>.</param>
    /// <returns><see langword="true"/> when ignored.</returns>
    public bool Matches(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        foreach (string name in path.Split('/'))
        {
            if (FileNameRule.IsReserved(name) || names.Contains(name) || extensions.Any(e => name.Length > e.Length && name.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }

    /// <inheritdoc />
    public override string ToString() => Patterns.Count == 0 ? "no ignore patterns" : string.Join(", ", Patterns);
}

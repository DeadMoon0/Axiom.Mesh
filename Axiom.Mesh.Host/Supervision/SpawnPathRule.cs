using System;
using System.IO;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Host;

/// <summary>
/// Where a spawned service's executable is (S-D11): an absolute path as written; a bare file name beside the daemon's own
/// executable, with <c>.exe</c> added on Windows when it has no extension, so that one default <c>config.json</c> serves
/// every platform an installed zip runs on.
/// </summary>
internal static class SpawnPathRule
{
    /// <summary>Whether a <c>spawn</c> value is one of the two forms: an absolute path, or a file name without a directory.</summary>
    /// <param name="spawn">The configured value.</param>
    /// <returns><see langword="true"/> for an absolute path or a bare file name.</returns>
    public static bool IsAbsoluteOrBareName(string spawn) =>
        !string.IsNullOrWhiteSpace(spawn)
        && (Path.IsPathFullyQualified(spawn) || (spawn.IndexOfAny(['/', '\\']) < 0 && spawn is not ("." or "..") && spawn.IndexOfAny(Path.GetInvalidFileNameChars()) < 0));

    /// <summary>The executable a <c>spawn</c> value names.</summary>
    /// <param name="spawn">The configured value: an absolute path or a bare file name.</param>
    /// <param name="hostDirectory">The directory the daemon's executable is in.</param>
    /// <param name="platform">The platform.</param>
    /// <returns>The absolute path.</returns>
    /// <exception cref="ArgumentException">The value is neither form; the configuration check refuses it first.</exception>
    public static string Resolve(string spawn, string hostDirectory, LocalPlatform platform)
    {
        ArgumentException.ThrowIfNullOrEmpty(hostDirectory);
        if (!IsAbsoluteOrBareName(spawn))
            throw new ArgumentException($"\"{spawn}\" is neither an absolute path nor a bare file name.", nameof(spawn));
        if (Path.IsPathFullyQualified(spawn))
            return spawn;
        string name = platform == LocalPlatform.Windows && Path.GetExtension(spawn).Length == 0 ? spawn + ".exe" : spawn;
        return Path.Combine(hostDirectory, name);
    }
}

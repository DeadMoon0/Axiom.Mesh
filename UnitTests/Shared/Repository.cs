using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Axiom.Mesh.Tests.Shared;

/// <summary>Where the repository and its projects are on disk, for the conventions that read sources and documents.</summary>
internal static class Repository
{
    private const string SolutionFile = "Axiom.Mesh.slnx";

    /// <summary>The repository root: the first folder above the test's output that holds the solution file.</summary>
    public static string Root { get; } = FindRoot();

    /// <summary>The folder of the project whose file is <c>&lt;assemblyName&gt;.csproj</c>, outside the spikes.</summary>
    /// <param name="assemblyName">The project's assembly name, which every project here keeps equal to its file name.</param>
    /// <returns>The absolute folder.</returns>
    public static string ProjectFolder(string assemblyName)
    {
        string[] found = [.. SourceFiles("*.csproj").Where(p => Path.GetFileNameWithoutExtension(p) == assemblyName)];
        Assert.True(found.Length == 1, $"Expected one project file {assemblyName}.csproj under {Root}, found {found.Length}.");
        return Path.GetDirectoryName(found[0])!;
    }

    /// <summary>Every file matching <paramref name="pattern"/> under the root, without build output, spikes or git internals.</summary>
    /// <param name="pattern">A file pattern such as <c>*.cs</c>.</param>
    /// <returns>Absolute paths, in ordinal order.</returns>
    public static IReadOnlyList<string> SourceFiles(string pattern) => SourceFiles(Root, pattern);

    /// <summary>Every file matching <paramref name="pattern"/> under <paramref name="folder"/>, without build output.</summary>
    /// <param name="folder">The folder to search, recursively.</param>
    /// <param name="pattern">A file pattern such as <c>*.cs</c>.</param>
    /// <returns>Absolute paths, in ordinal order.</returns>
    public static IReadOnlyList<string> SourceFiles(string folder, string pattern) =>
        [.. Directory.EnumerateFiles(folder, pattern, SearchOption.AllDirectories)
            .Where(p => !IsExcluded(Path.GetRelativePath(Root, p)))
            .Order(StringComparer.Ordinal)];

    /// <summary>The path relative to the root, with forward slashes, for messages and documents.</summary>
    /// <param name="path">An absolute path under the root.</param>
    /// <returns>The relative path.</returns>
    public static string Relative(string path) => Path.GetRelativePath(Root, path).Replace('\\', '/');

    private static bool IsExcluded(string relative)
    {
        string[] segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments[0] is "Spikes" or ".git" or "artifacts"
            || segments.Any(s => s is "bin" or "obj" or "node_modules" or "BenchmarkDotNet.Artifacts");
    }

    private static string FindRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFile)))
                return directory.FullName;
        }

        throw new InvalidOperationException($"The repository root ({SolutionFile}) was not found above {AppContext.BaseDirectory}.");
    }
}

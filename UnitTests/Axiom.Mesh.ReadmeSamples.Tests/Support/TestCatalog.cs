using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Axiom.Mesh.Tests.Shared;

namespace Axiom.Mesh.ReadmeSamples.Tests.Support;

/// <summary>Whether the tests a document names exist in the repository's test sources.</summary>
internal static class TestCatalog
{
    /// <summary>The references among <paramref name="references"/> no test source declares.</summary>
    /// <param name="references">What a document names.</param>
    /// <returns>The missing ones, as written.</returns>
    public static IReadOnlyList<string> Missing(IEnumerable<TestReference> references) =>
        [.. references.Where(r => !Exists(r)).Select(r => r.ToString())];

    private static bool Exists(TestReference reference) =>
        Folder(reference.Project) is { } folder
        && Repository.SourceFiles(folder, "*.cs").Select(File.ReadAllText).Any(text =>
            Regex.IsMatch(text, @"\bclass " + Regex.Escape(reference.Type) + @"\b")
            && Regex.IsMatch(text, @"\b(void|Task) " + Regex.Escape(reference.Method) + @"\("));

    // "Model.Tests" is UnitTests/Axiom.Mesh.Model.Tests (or under Apps/, or IntegrationTests/); "Shared" is UnitTests/Shared.
    private static string? Folder(string project)
    {
        if (project == "Shared")
            return Path.Combine(Repository.Root, "UnitTests", "Shared");

        string name = "Axiom.Mesh." + project;
        return Repository.SourceFiles("*.csproj")
            .Where(p => Path.GetFileNameWithoutExtension(p) == name)
            .Select(Path.GetDirectoryName)
            .FirstOrDefault();
    }
}

using System;
using System.IO;
using System.Linq;
using Axiom.Mesh.ReadmeSamples.Tests.Support;
using Axiom.Mesh.Tests.Shared;

namespace Axiom.Mesh.ReadmeSamples.Tests.Conventions;

/// <summary>Conventions over every source file of the repository, outside the spikes and the build output.</summary>
public sealed class RepositoryConventionTests
{
    // A type is found by its file name, and a file is reviewed as one unit; two types in one file are found by neither.
    [Fact]
    public void EveryFileDeclaresExactlyOneTopLevelTypeNamedLikeTheFile()
    {
        string[] offenders = [.. Repository.SourceFiles("*.cs")
            .Where(p => !p.EndsWith(".g.cs", StringComparison.Ordinal))
            .Select(p => (Path: p, Problem: SourceFileShape.Problem(Path.GetFileNameWithoutExtension(p), File.ReadAllText(p))))
            .Where(f => f.Problem is not null)
            .Select(f => $"{Repository.Relative(f.Path)} {f.Problem}")];

        Assert.True(offenders.Length == 0, "Files that break one-type-per-file: " + string.Join("; ", offenders));
    }
}

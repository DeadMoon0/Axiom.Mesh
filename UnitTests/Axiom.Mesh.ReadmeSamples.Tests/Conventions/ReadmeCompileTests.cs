using System;
using System.IO;
using System.Linq;
using Axiom.Mesh.ReadmeSamples.Tests.Support;
using Axiom.Mesh.Tests.Shared;

namespace Axiom.Mesh.ReadmeSamples.Tests.Conventions;

/// <summary>
/// Every <c>```csharp</c> block of the root README and of every package README compiles against the packages. A README
/// block is code a reader copies, so it may not lean on anything the README does not show.
/// </summary>
public sealed class ReadmeCompileTests
{
    public static TheoryData<string> Readmes()
    {
        var readmes = new TheoryData<string>();
        foreach (string readme in Repository.SourceFiles("README.md")
            .Select(Repository.Relative)
            .Where(p => p == "README.md" || p.StartsWith("Axiom.Mesh.", StringComparison.Ordinal) || p.StartsWith("Apps/", StringComparison.Ordinal)))
            readmes.Add(readme);
        return readmes;
    }

    [Theory]
    [MemberData(nameof(Readmes))]
    public void EveryCSharpBlock_Compiles(string readme)
    {
        var errors = ReadmeCompiler.Errors(ReadmeCompiler.Blocks(File.ReadAllText(Path.Combine(Repository.Root, readme))));
        Assert.True(errors.Count == 0, readme + ":" + Environment.NewLine + string.Join(Environment.NewLine, errors));
    }

    // Every package ships a README (it is packed); a package without one would silently drop out of the theory above.
    [Fact]
    public void EveryPackageHasAReadme()
    {
        string[] packages = [.. Repository.SourceFiles("*.csproj")
            .Where(p => !Path.GetFileName(p).EndsWith(".Tests.csproj", StringComparison.Ordinal))
            .Where(p => !File.Exists(Path.Combine(Path.GetDirectoryName(p)!, "README.md")))
            .Select(Repository.Relative)];
        Assert.True(packages.Length == 0, "Packages without a README.md: " + string.Join(", ", packages));
    }
}

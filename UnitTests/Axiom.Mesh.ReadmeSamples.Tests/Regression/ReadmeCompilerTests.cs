using Axiom.Mesh.ReadmeSamples.Tests.Support;

namespace Axiom.Mesh.ReadmeSamples.Tests.Regression;

/// <summary>The README compiler finds blocks, compiles them against the packages, and reports what does not compile.</summary>
public sealed class ReadmeCompilerTests
{
    // Damage: the block pattern stops matching or the references go missing, and every README "compiles" because no
    // block is compiled at all, or none can be.
    [Fact]
    public void ABlockUsingAPackageCompiles_AndABrokenBlockIsReportedWithItsLine()
    {
        const string Readme =
            "Text\n\n```csharp\nusing Axiom.Mesh;\n\nConsole.WriteLine(Limits.MaxFrameLength);\n```\n\n" +
            "```csharp\nint broken = \"text\";\n```\n\n```text\nnot code\n```\n";

        var blocks = ReadmeCompiler.Blocks(Readme);
        var errors = ReadmeCompiler.Errors(blocks);

        Assert.Equal(2, blocks.Count);
        Assert.True(errors.Count == 1, string.Join(" | ", errors));
        Assert.StartsWith("block 2, line 1:", errors[0], System.StringComparison.Ordinal);
    }
}

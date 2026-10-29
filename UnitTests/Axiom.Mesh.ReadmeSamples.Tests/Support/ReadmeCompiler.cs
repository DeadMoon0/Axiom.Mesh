using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Axiom.Mesh.ReadmeSamples.Tests.Support;

/// <summary>
/// Compiles the <c>```csharp</c> blocks of one README the way a reader would paste them into a new project:
/// <list type="bullet">
/// <item>The SDK's implicit usings apply (<c>System</c>, <c>System.IO</c>, <c>System.Linq</c>, <c>System.Threading.Tasks</c>, ...).</item>
/// <item>The <c>using</c> directives of all blocks of a README apply to each of its blocks (the quick start lists them).</item>
/// <item>Types a README declares in one block are known in its other blocks.</item>
/// <item>Everything else a block declares itself.</item>
/// </list>
/// Text that is not meant to compile goes in a <c>```text</c> block.
/// </summary>
internal static partial class ReadmeCompiler
{
    /// <summary>What Microsoft.NET.Sdk adds to every project with <c>ImplicitUsings</c> on, the default of <c>dotnet new</c>.</summary>
    private static readonly string[] ImplicitUsings = ["System", "System.Collections.Generic", "System.IO", "System.Linq", "System.Net.Http", "System.Threading", "System.Threading.Tasks"];

    private static readonly CSharpParseOptions Parse = new(LanguageVersion.Latest);

    /// <summary>Every assembly the test runs with: the framework, the Mesh packages and their dependencies.</summary>
    private static readonly Lazy<MetadataReference[]> References = new(() =>
        [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(p => !Path.GetFileName(p).StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))]);

    /// <summary>The C# blocks of a markdown text.</summary>
    /// <param name="markdown">The README's text.</param>
    /// <returns>The code of each block, in order.</returns>
    public static IReadOnlyList<string> Blocks(string markdown) =>
        [.. CSharpBlock().Matches(markdown).Select(m => m.Groups["code"].Value)];

    /// <summary>The compile errors of every block, each as <c>block N, line L: message</c>.</summary>
    /// <param name="blocks">The blocks of one README.</param>
    /// <returns>The errors; empty when every block compiles.</returns>
    public static IReadOnlyList<string> Errors(IReadOnlyList<string> blocks)
    {
        CompilationUnitSyntax[] units = [.. blocks.Select(b => (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(b, Parse).GetRoot())];
        SyntaxTree usings = CSharpSyntaxTree.ParseText(
            string.Concat(ImplicitUsings.Concat(units.SelectMany(u => u.Usings).Select(u => u.Name!.ToString()))
                .Distinct(StringComparer.Ordinal).Select(n => "global using " + n + ";\n")),
            Parse);

        var errors = new List<string>();
        for (int i = 0; i < units.Length; i++)
        {
            foreach (Diagnostic error in Compile(units, i, usings))
                errors.Add($"block {i + 1}, line {error.Location.GetLineSpan().StartLinePosition.Line + 1}: {error.GetMessage()}");
        }

        return errors;
    }

    private static IEnumerable<Diagnostic> Compile(CompilationUnitSyntax[] units, int index, SyntaxTree usings)
    {
        CompilationUnitSyntax unit = units[index];
        HashSet<string> declared = [.. TypesOf(unit).Select(t => t.Identifier.Text)];
        MemberDeclarationSyntax[] borrowed = [.. units.Where((_, j) => j != index).SelectMany(TypesOf).Where(t => declared.Add(t.Identifier.Text))];
        SyntaxTree others = CSharpSyntaxTree.Create(SyntaxFactory.CompilationUnit().WithMembers(SyntaxFactory.List(borrowed)), Parse);

        bool program = unit.Members.OfType<GlobalStatementSyntax>().Any();
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReadmeBlock" + index,
            [usings, unit.SyntaxTree, others],
            References.Value,
            new CSharpCompilationOptions(program ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        return compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error);
    }

    private static IEnumerable<BaseTypeDeclarationSyntax> TypesOf(CompilationUnitSyntax unit) => unit.Members.OfType<BaseTypeDeclarationSyntax>();

    [GeneratedRegex(@"^```csharp\r?\n(?<code>.*?)^```", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex CSharpBlock();
}

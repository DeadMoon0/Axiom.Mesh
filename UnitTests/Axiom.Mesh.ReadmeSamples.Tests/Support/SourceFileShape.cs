using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Axiom.Mesh.ReadmeSamples.Tests.Support;

/// <summary>The top-level types a C# file declares, read with Roslyn, as a pure function of its name and text.</summary>
internal static class SourceFileShape
{
    /// <summary>
    /// Why a file breaks "one top-level type, named like the file", or <see langword="null"/> when it does not. A generic
    /// type may live in <c>NameOfT.cs</c> (any suffix after <c>Of</c>), so <c>Foo</c> and <c>Foo&lt;T&gt;</c> can sit side by side.
    /// </summary>
    /// <param name="fileName">The file's name without folder and extension.</param>
    /// <param name="source">The file's text.</param>
    /// <returns>The reason, or <see langword="null"/>.</returns>
    public static string? Problem(string fileName, string source)
    {
        var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)).GetRoot();
        var types = TopLevel(root.Members).ToList();
        if (types.Count != 1)
            return $"declares {types.Count} top-level types ({string.Join(", ", types.Select(t => t.Name))})";

        (string name, bool generic) = types[0];
        bool matches = fileName == name || (generic && fileName.StartsWith(name + "Of", StringComparison.Ordinal));
        return matches ? null : $"declares {name}, not {fileName}";
    }

    private static IEnumerable<(string Name, bool Generic)> TopLevel(IEnumerable<MemberDeclarationSyntax> members)
    {
        foreach (MemberDeclarationSyntax member in members)
        {
            switch (member)
            {
                case BaseNamespaceDeclarationSyntax ns:
                    foreach (var inner in TopLevel(ns.Members))
                        yield return inner;
                    break;
                case TypeDeclarationSyntax type:
                    yield return (type.Identifier.Text, type.TypeParameterList is not null);
                    break;
                case EnumDeclarationSyntax enumeration:
                    yield return (enumeration.Identifier.Text, false);
                    break;
                case DelegateDeclarationSyntax @delegate:
                    yield return (@delegate.Identifier.Text, @delegate.TypeParameterList is not null);
                    break;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Axiom.Mesh.Tests.Shared;

/// <summary>
/// The rules of a test suite, which the compiler cannot check: every test belongs to exactly one of the three parts
/// (business rules, regression, conventions), no test waits by the wall clock, a regression test says what damage it
/// pins, and a theory row says which case it is. Every test project derives this once, for its own sources.
/// </summary>
public abstract class TestSuiteConventionTests
{
    private static readonly string[] Parts = ["Rules", "Regression", "Conventions"];

    private Assembly Suite => GetType().Assembly;

    private string SuiteFolder => Repository.ProjectFolder(Suite.GetName().Name!);

    private IReadOnlyList<string> SuiteSources => Repository.SourceFiles(SuiteFolder, "*.cs");

    // A test that serves two parts serves neither; the folder (and with it the namespace) says which one it serves.
    [Fact]
    public void EveryTestClassLivesInRulesRegressionOrConventions()
    {
        var outsideByNamespace = Suite.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && DeclaresOrInheritsTests(t))
            .Where(t => !Parts.Contains(t.Namespace?.Split('.')[^1], StringComparer.Ordinal))
            .Select(t => t.FullName ?? t.Name);
        var outsideByFolder = SuiteSources
            .Where(f => TestSourceRules.DeclaresTests(File.ReadAllText(f)))
            .Where(f => !Parts.Contains(Path.GetRelativePath(SuiteFolder, f).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0], StringComparer.Ordinal))
            .Select(Repository.Relative);

        string[] offenders = [.. outsideByNamespace, .. outsideByFolder];
        Assert.True(offenders.Length == 0, "Tests outside Rules/, Regression/ and Conventions/: " + string.Join(", ", offenders));
    }

    [Fact]
    public void NoTestUsesThreadSleepOrTaskDelay()
    {
        string[] offenders = [.. Offenders(SuiteSources, TestSourceRules.SleepsAndDelays)];
        Assert.True(offenders.Length == 0, "Tests that wait by the wall clock: " + string.Join(", ", offenders));
    }

    [Fact]
    public void EveryRegressionTestNamesTheDamageItPins()
    {
        string regression = Path.Combine(SuiteFolder, "Regression");
        IReadOnlyList<string> files = Directory.Exists(regression) ? Repository.SourceFiles(regression, "*.cs") : [];
        string[] offenders = [.. Offenders(files, TestSourceRules.TestsWithoutDamage)];
        Assert.True(offenders.Length == 0, "Regression tests without a '// Damage:' line: " + string.Join(", ", offenders));
    }

    [Fact]
    public void EveryTheoryRowCarriesAComment()
    {
        string[] offenders = [.. Offenders(SuiteSources, TestSourceRules.TheoryRowsWithoutComment)];
        Assert.True(offenders.Length == 0, "Theory rows without a comment: " + string.Join(", ", offenders));
    }

    private static IEnumerable<string> Offenders(IEnumerable<string> files, Func<string, IReadOnlyList<int>> rule) =>
        files.SelectMany(f => rule(File.ReadAllText(f)).Select(line => $"{Repository.Relative(f)}:{line}"));

    private static bool DeclaresOrInheritsTests(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Any(m => m.GetCustomAttributes(inherit: true).Any(a => a is FactAttribute));
}

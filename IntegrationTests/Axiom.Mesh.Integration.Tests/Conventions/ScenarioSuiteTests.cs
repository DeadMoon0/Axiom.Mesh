using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Axiom.Mesh.Integration.Tests.Rules;
using Axiom.Mesh.Testing.Tests.Rules;
using Axiom.Mesh.Tests.Shared;

namespace Axiom.Mesh.Integration.Tests.Conventions;

/// <summary>
/// The promise "the same scenario passes in memory and on sockets" holds only while both suites run the same scenarios:
/// this suite compiles the in-memory suite's own scenario file, unchanged, and its socket suite runs every scenario in it.
/// </summary>
public sealed class ScenarioSuiteTests
{
    private const string ScenarioFile = "UnitTests/Axiom.Mesh.Testing.Tests/Rules/MeshScenarios.cs";

    [Fact]
    public void TheSocketSuiteCompilesTheInMemorySuitesScenarioFileAndRunsEveryScenarioInIt()
    {
        string project = File.ReadAllText(Path.Combine(Repository.ProjectFolder("Axiom.Mesh.Integration.Tests"), "Axiom.Mesh.Integration.Tests.csproj"));
        string[] scenarios = [.. typeof(MeshScenarios).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<FactAttribute>().Any())
            .Select(m => m.Name)];
        string[] run = [.. typeof(SocketScenarios).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttributes<FactAttribute>().Any() && m.DeclaringType == typeof(MeshScenarios))
            .Select(m => m.Name)];

        Assert.Contains("..\\..\\" + ScenarioFile.Replace('/', '\\'), project, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(Repository.Root, ScenarioFile)));
        Assert.Equal(14, scenarios.Length);
        Assert.Equal(scenarios.Order(), run.Order());
        Assert.DoesNotContain(typeof(SocketScenarios).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly), m => m.GetCustomAttributes<FactAttribute>().Any());
    }
}

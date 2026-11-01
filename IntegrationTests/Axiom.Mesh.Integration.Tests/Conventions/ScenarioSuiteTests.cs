using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Axiom.Mesh.Integration.Tests.Rules;
using Axiom.Mesh.Chat.Tests.Rules;
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
        Assert.Equal(15, scenarios.Length);
        Assert.Equal(scenarios.Order(), run.Order());
        Assert.DoesNotContain(typeof(SocketScenarios).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly), m => m.GetCustomAttributes<FactAttribute>().Any());
    }

    // The same for the chat service's suite (plan §9.3 item 5): scenarios 1 to 13 run in memory and on sockets alike.
    [Fact]
    public void TheSocketSuiteCompilesTheChatScenarioFileAndRunsEveryScenarioInIt()
    {
        const string ChatFile = "UnitTests/Services/Axiom.Mesh.Chat.Tests/Rules/ChatScenarios.cs";
        string project = File.ReadAllText(Path.Combine(Repository.ProjectFolder("Axiom.Mesh.Integration.Tests"), "Axiom.Mesh.Integration.Tests.csproj"));
        string[] scenarios = [.. typeof(ChatScenarios).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<FactAttribute>().Any())
            .Select(m => m.Name)];
        string[] run = [.. typeof(SocketChatScenarios).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttributes<FactAttribute>().Any() && m.DeclaringType == typeof(ChatScenarios))
            .Select(m => m.Name)];

        Assert.Contains("..\\..\\" + ChatFile.Replace('/', '\\'), project, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(Repository.Root, ChatFile)));
        Assert.Equal(13, scenarios.Length);
        Assert.Equal(scenarios.Order(), run.Order());
        Assert.DoesNotContain(typeof(SocketChatScenarios).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly), m => m.GetCustomAttributes<FactAttribute>().Any());
    }
}

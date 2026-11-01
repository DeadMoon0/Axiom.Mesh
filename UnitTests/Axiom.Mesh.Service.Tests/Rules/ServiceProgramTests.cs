using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB;
using Axiom.LDB.Storage.InMemory;
using Axiom.Mesh.Testing;

namespace Axiom.Mesh.Service.Tests.Rules;

/// <summary>
/// The command line of a service executable (LOCAL-SPEC.md §9.1, plan §9.2 item 4): <c>version</c> prints the name and
/// version; anything but <c>run</c> prints the usage; a run without a token or with an unusable definition exits
/// <see cref="ServiceExitCode.Configuration"/> before it connects, so that its supervisor does not restart it in a loop.
/// </summary>
public sealed class ServiceProgramTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    // version prints "<name> <version>" on standard output and exits Ok.
    [Fact]
    public async Task VersionPrintsTheNameAndVersion()
    {
        using var output = new StringWriter();

        ServiceExitCode exit = await ServiceProgram.RunAsync(new TestEchoService(), ["version"], output, TextWriter.Null, Offline(), None);

        Assert.Equal(ServiceExitCode.Ok, exit);
        Assert.Equal("echo 1.2.3", output.ToString().Trim());
    }

    // An argument the program does not know prints the usage on standard error and exits Usage.
    [Fact]
    public async Task AnUnknownCommandPrintsTheUsage()
    {
        using var error = new StringWriter();

        ServiceExitCode exit = await ServiceProgram.RunAsync(new TestEchoService(), ["serve", "now"], TextWriter.Null, error, Offline(), None);

        Assert.Equal(ServiceExitCode.Usage, exit);
        Assert.Contains("usage: echo [run]", error.ToString(), StringComparison.Ordinal);
    }

    // §4.2: a service with no token (none in its environment, none in the configuration) cannot prove its name: it exits
    // Configuration without connecting.
    [Fact]
    public async Task ARunWithoutATokenExitsConfigurationWithoutConnecting()
    {
        int connects = 0;
        var environment = new ServiceEnvironment
        {
            Version = "1.2.3",
            HostAddress = "nowhere",
            ConnectHost = _ =>
            {
                Interlocked.Increment(ref connects);
                throw new IOException("no Host");
            },
            OpenState = ct => Database.OpenAsync(new InMemoryStorageEnvironment(), DatabaseOptions.Default, ct),
        };

        ServiceExitCode exit = await ServiceProgram.RunAsync(new TestEchoService(), ["run"], TextWriter.Null, TextWriter.Null, environment, None);

        Assert.Equal((ServiceExitCode.Configuration, 0), (exit, connects));
    }

    [Theory]
    [InlineData("", "a name")] // a service needs a name
    [InlineData("a-name-that-is-longer-than-32-bytes", "the name's length")] // a service name is at most 32 bytes (§3.2)
    [InlineData("chat app", "the name's characters")] // letters, digits, '.', '-' and '_' only
    [InlineData("chat", "no kind")] // a service defines at least one Package kind
    public async Task AnUnusableDefinitionExitsConfiguration(string name, string flaw)
    {
        ServiceDefinition definition = TestEchoService.Echo with { Name = name, Kinds = flaw == "no kind" ? [] : TestEchoService.Echo.Kinds };

        ServiceExitCode exit = await ServiceProgram.RunAsync(new TestEchoService(definition), ["run"], TextWriter.Null, TextWriter.Null, Offline(), None);

        Assert.Equal(ServiceExitCode.Configuration, exit);
        Assert.NotEmpty(definition.Problems());
    }

    // An environment with a token whose Host is never reached; enough for everything that ends before connecting.
    private static ServiceEnvironment Offline() => new()
    {
        Version = "1.2.3",
        HostAddress = "nowhere",
        ConnectHost = _ => throw new IOException("no Host"),
        OpenState = ct => Database.OpenAsync(new InMemoryStorageEnvironment(), DatabaseOptions.Default, ct),
        ReadConfig = () => TestServices.ConfigOf("echo"),
    };
}

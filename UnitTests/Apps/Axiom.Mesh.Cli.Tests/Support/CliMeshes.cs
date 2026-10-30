using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli.Tests.Support;

/// <summary>In-memory meshes whose nodes configure the external service <c>cli</c>, as a Host's default <c>config.json</c> does (decision D4).</summary>
internal static class CliMeshes
{
    /// <summary>The <c>cli</c> service's token on every node.</summary>
    public static ServiceToken Token { get; } = ServiceToken.FromBytes(Enumerable.Repeat((byte)0x11, 32).ToArray());

    /// <summary>The local port's configuration: the <c>cli</c> service with the Host's default kinds.</summary>
    public static LocalPortOptions Local { get; } = new()
    {
        Services =
        [
            new LocalService
            {
                Name = "cli",
                Provenance = ServiceProvenance.External,
                Token = Token,
                Kinds = [PackageKind.Archive, PackageKind.Channel],
                EntryKinds = [EntryKind.Version, EntryKind.Message, EntryKind.Redaction],
            },
        ],
    };

    /// <summary>A mesh of nodes with the <c>cli</c> service.</summary>
    /// <param name="options">The mesh's nodes and trust.</param>
    /// <returns>The running mesh.</returns>
    public static Task<TestMesh> StartAsync(TestMeshOptions options) => TestMesh.StartAsync(options with { Local = Local }, CancellationToken.None);

    /// <summary>The full hex a <c>Name  value</c> line of a command's output shows.</summary>
    /// <param name="result">The command's result.</param>
    /// <param name="name">The line's name.</param>
    /// <returns>The value.</returns>
    public static string Value(CliResult result, string name)
    {
        Match match = Regex.Match(result.Stdout, "^" + Regex.Escape(name) + @"\s+(\S+)", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value : throw new InvalidOperationException($"No line '{name}' in:\n{result}");
    }

    /// <summary>Asserts a command succeeded and returns its result.</summary>
    /// <param name="run">The command running.</param>
    /// <returns>Its result.</returns>
    public static async Task<CliResult> OkAsync(Task<CliResult> run)
    {
        CliResult result = await run;
        Assert.True(result.ExitCode == CliExitCode.Ok, result.ToString());
        return result;
    }
}

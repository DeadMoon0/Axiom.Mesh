namespace Axiom.Mesh.Cli.Tests.Rules;

/// <summary>
/// The command line finds a Host whose pipe <c>config.json</c> names (<c>pipe.name</c>), as the Host listens on it:
/// <c>AXIOM_MESH_PIPE</c>, else <c>pipe.name</c>, else the default (STRUCTURAL-DEBT.md 48; the precedence itself is
/// <c>Local.Tests: PipeLocationTests</c>).
/// </summary>
public sealed class PipeNameTests
{
    [Theory]
    [InlineData("""{ "pipe": { "name": "axiom-mesh-home" } }""", "axiom-mesh-home")] // the configured name
    [InlineData("""{ "pipe": { "name": "/home/u/mesh.sock", "allowUsers": [] }, "mesh": { "port": 0 } }""", "/home/u/mesh.sock")] // beside other keys
    [InlineData("""{ /* comment */ "pipe": { "name": "x", }, }""", "x")] // comments and trailing commas, as the Host reads them
    [InlineData("""{ "pipe": { "name": "" } }""", null)] // an empty name is none
    [InlineData("""{ "pipe": { } }""", null)] // no name
    [InlineData("""{ "pipe": "axiom-mesh" }""", null)] // another shape
    [InlineData("{ not json", null)] // not JSON
    [InlineData(null, null)] // no config.json
    public void PipeNameIsReadFromConfigJson(string? json, string? expected)
    {
        Assert.Equal(expected is not null, CliConfig.TryReadPipeName(json, out string? name));
        Assert.Equal(expected, name);
    }
}

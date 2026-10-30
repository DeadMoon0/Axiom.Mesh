using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli.Tests.Rules;

/// <summary>
/// The write commands present the token of the external service <c>cli</c> (decision D4; LOCAL-SPEC.md §4.2):
/// <c>AXIOM_MESH_SERVICE_TOKEN</c> when set, else <c>services.cli.token</c> of the Host's <c>config.json</c>.
/// </summary>
public sealed class ServiceTokenTests
{
    private const string Hex = "1111111111111111111111111111111111111111111111111111111111111111";
    private const string Other = "2222222222222222222222222222222222222222222222222222222222222222";

    [Fact]
    public void TheVariableWins_ElseConfigJsonsCliToken_ElseNone()
    {
        string config = $$"""{ "services": { "cli": { "external": true, "token": "{{Hex}}" } } }""";
        Assert.True(ServiceToken.TryParseHex(Hex, out ServiceToken fromConfig));
        Assert.True(ServiceToken.TryParseHex(Other, out ServiceToken fromVariable));

        Assert.Equal(fromVariable, CommandContext.TokenFrom(Other, config));
        Assert.Equal(fromConfig, CommandContext.TokenFrom(null, config));
        Assert.Equal(fromConfig, CommandContext.TokenFrom("not hex", config));
        Assert.True(CommandContext.TokenFrom(null, null).IsNone);
        Assert.True(CommandContext.TokenFrom(null, "{ not json").IsNone);
        Assert.True(CommandContext.TokenFrom(null, """{ "services": { "cli": { "token": "" } } }""").IsNone);
        Assert.True(CommandContext.TokenFrom(null, """{ "services": { "sync": { "token": "11" } } }""").IsNone);
    }
}

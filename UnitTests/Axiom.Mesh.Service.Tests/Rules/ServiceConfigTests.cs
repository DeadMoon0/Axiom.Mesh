using Axiom.Mesh.Local;

namespace Axiom.Mesh.Service.Tests.Rules;

/// <summary>
/// LOCAL-SPEC.md §4.2, an external service's token and the pipe's name as the Host's <c>config.json</c> holds them: read
/// when they are there and well formed, "not there" for anything else.
/// </summary>
public sealed class ServiceConfigTests
{
    private const string Token = "1111111111111111111111111111111111111111111111111111111111111111";

    [Theory]
    [InlineData("{ \"services\": { \"chat\": { \"token\": \"" + Token + "\" } } }", true)] // the service's own entry
    [InlineData("{ \"services\": { \"sync\": { \"token\": \"" + Token + "\" } } }", false)] // another service's token is not this one's
    [InlineData("{ \"services\": { \"chat\": { \"token\": \"not hex\" } } }", false)] // a token that is not 64 hex digits
    [InlineData("{ \"services\": { \"chat\": { \"token\": 7 } } }", false)] // a token that is not a string
    [InlineData("{ // a comment\n \"services\": { \"chat\": { \"token\": \"" + Token + "\", } } }", true)] // comments and trailing commas, as the Host writes and reads
    [InlineData("not json", false)] // a file that is not JSON
    [InlineData("", false)] // no file
    public void AnExternalServiceReadsItsOwnTokenFromConfigJson(string json, bool found)
    {
        bool read = ServiceConfig.TryReadToken(json, "chat", out ServiceToken token);

        Assert.Equal(found, read);
        Assert.Equal(found, !token.IsNone);
    }

    [Theory]
    [InlineData("{ \"pipe\": { \"name\": \"axiom-mesh-dev\" } }", "axiom-mesh-dev")] // a pipe of its own
    [InlineData("{ \"pipe\": { \"name\": \"\" } }", null)] // empty is no name
    [InlineData("{ \"pipe\": {} }", null)] // no name
    public void ThePipesNameIsReadWhenTheConfigurationNamesOne(string json, string? expected)
    {
        ServiceConfig.TryReadPipeName(json, out string? name);

        Assert.Equal(expected, name);
    }
}

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Axiom.Mesh.Core;
using Axiom.Mesh.Local;
using Axiom.Mesh.Service;

namespace Axiom.Mesh.Testing;

/// <summary>
/// Services as a test mesh's local port lists them (LOCAL-SPEC.md §4.2): external, each with a published token derived from
/// its name, so that a service run in process (<see cref="TestNode.RunServiceAsync"/>) proves its name the way a program
/// the user starts does.
/// </summary>
public static class TestServices
{
    private const string TokenDomain = "axiom-mesh-test-service/";

    /// <summary>The published token of a test service: SHA-256 of <c>axiom-mesh-test-service/&lt;name&gt;</c>. It guards nothing outside a test.</summary>
    /// <param name="serviceName">The service's name.</param>
    /// <returns>The token.</returns>
    public static ServiceToken TokenOf(string serviceName)
    {
        ArgumentException.ThrowIfNullOrEmpty(serviceName);
        return ServiceToken.FromBytes(SHA256.HashData(Encoding.UTF8.GetBytes(TokenDomain + serviceName)));
    }

    /// <summary>The configuration entry of a service: external, its published token, the kinds of its definition.</summary>
    /// <param name="definition">The service's definition.</param>
    /// <returns>The entry.</returns>
    public static LocalService External(ServiceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return new LocalService
        {
            Name = definition.Name,
            Provenance = ServiceProvenance.External,
            Token = TokenOf(definition.Name),
            Kinds = [.. definition.Kinds.Select(k => k.Kind).Order()],
            EntryKinds = definition.EntryKinds,
        };
    }

    /// <summary>A local port that also lists the given services as external, each with its published token.</summary>
    /// <param name="local">The local port's configuration so far, for example <see cref="TestCliService.Local"/>.</param>
    /// <param name="services">The services' definitions.</param>
    /// <returns>The configuration.</returns>
    public static LocalPortOptions With(LocalPortOptions local, params ServiceDefinition[] services)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(services);
        return local with { Services = [.. local.Services, .. services.Select(External)] };
    }

    /// <summary>The <c>config.json</c> text a service run by hand reads its token from (<c>services.&lt;name&gt;.token</c>).</summary>
    /// <param name="serviceName">The service's name.</param>
    /// <returns>The text.</returns>
    public static string ConfigOf(string serviceName)
    {
        Span<byte> bytes = stackalloc byte[32];
        TokenOf(serviceName).CopyTo(bytes);
        return $"{{ \"services\": {{ \"{serviceName}\": {{ \"external\": true, \"token\": \"{Convert.ToHexString(bytes).ToLowerInvariant()}\" }} }} }}";
    }
}

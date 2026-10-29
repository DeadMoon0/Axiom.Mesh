using System;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;

namespace Axiom.Mesh.Local.Tests.Conventions;

/// <summary>
/// No key crosses the pipe (LOCAL-SPEC.md §13): signing happens in the Host, so no record, request, event or message of
/// the local protocol has a field that could carry a private key.
/// </summary>
public sealed class NoKeyCrossesThePipeTests
{
    private static readonly string[] KeyWords = ["private", "secret", "pkcs8", "pkcs12", "seed", "signer"];

    [Fact]
    public void NoRecordHasAFieldThatCanHoldAPrivateKey()
    {
        var offenders = typeof(LocalProtocol).Assembly.GetExportedTypes()
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => (Type: t, Property: p)))
            .Where(x => IsKeyType(x.Property.PropertyType) || KeyWords.Any(w => x.Property.Name.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .Select(x => $"{x.Type.Name}.{x.Property.Name}")
            .ToList();

        Assert.True(offenders.Count == 0, "Fields that could carry a private key: " + string.Join(", ", offenders));
    }

    private static bool IsKeyType(Type type) =>
        typeof(AsymmetricAlgorithm).IsAssignableFrom(type) || typeof(ISigner).IsAssignableFrom(type);
}

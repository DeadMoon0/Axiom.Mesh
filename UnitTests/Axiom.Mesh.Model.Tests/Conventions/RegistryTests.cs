using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Axiom.Mesh.Tests.Support;

namespace Axiom.Mesh.Tests.Conventions;

/// <summary>
/// The registries of SPEC.md §14 that Model carries (object tags, Entry kinds, Package kinds, signature and hash
/// domains) agree with the code in both directions: a value added to one and not the other fails here.
/// </summary>
public sealed class RegistryTests
{
    private static readonly string Spec = SpecTables.Document("SPEC.md");

    [Fact]
    public void EveryRegistryValueOfSpec14AppearsExactlyOnce()
    {
        AssertMatches<ObjectTag>(SpecTables.Rows(Spec, "### 14.6 "), nameColumn: 1);
        AssertMatches<EntryKind>(SpecTables.Rows(Spec, "### 14.7 "), nameColumn: 1);
        AssertMatches<PackageKind>(SpecTables.Rows(Spec, "### 14.8 "), nameColumn: 1);

        var domains = SpecTables.Rows(Spec, "### 14.5 ").ToDictionary(r => r[0], r => r[1].Trim('`'), StringComparer.Ordinal);
        var code = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Package id"] = PackageId.DerivationDomain,
            ["Entry"] = SigningDomain.Entry.Name,
            ["Manifest"] = SigningDomain.Manifest.Name,
            ["Roster"] = SigningDomain.Roster.Name,
            ["Grant"] = SigningDomain.Grant.Name,
        };
        Assert.Equal(domains.OrderBy(d => d.Key, StringComparer.Ordinal), code.OrderBy(d => d.Key, StringComparer.Ordinal));
        Assert.Equal(4, typeof(SigningDomain).GetProperties().Count(p => p.PropertyType == typeof(SigningDomain)));
    }

    // Each tag of §14.6 is parsed by exactly one type, named like the object (its unverified state for a signed one),
    // and every other tag is opaque (§1.4).
    [Fact]
    public void EveryObjectTagOfSpec14HasExactlyOneCodec()
    {
        var parsers = new Dictionary<byte, Type>();
        foreach (ParsedObject sample in ObjectProtocol.Samples())
        {
            Assert.True(Objects.TryParse(HashedObject.Of(sample.Bytes), out ParsedObject? parsed, out _));
            if (parsers.TryGetValue((byte)parsed.Tag, out Type? known))
                Assert.Equal(known, parsed.GetType());
            parsers[(byte)parsed.Tag] = parsed.GetType();
        }

        foreach (string[] row in SpecTables.Rows(Spec, "### 14.6 "))
        {
            byte tag = byte.Parse(row[0], CultureInfo.InvariantCulture);
            Assert.True(parsers.TryGetValue(tag, out Type? type), $"No sample parses as tag {tag} ({row[1]}).");
            Assert.True(type.Name == row[1] || type.Name == "Unverified" + row[1], $"Tag {tag} ({row[1]}) is parsed as {type.Name}.");
        }

        Assert.Equal(parsers.Values.Count(t => t != typeof(OpaqueObject)), parsers.Values.Where(t => t != typeof(OpaqueObject)).Distinct().Count());
        foreach (byte unknown in new byte[] { 0, 8, 9, 255 })
            Assert.IsType<OpaqueObject>(Samples.Parse<ParsedObject>(RawObjects.Unsigned(unknown, _ => { })));
    }

    private static void AssertMatches<TEnum>(IReadOnlyList<string[]> rows, int nameColumn)
        where TEnum : struct, Enum
    {
        var specified = rows.Select(r => (Value: int.Parse(r[0], CultureInfo.InvariantCulture), Name: r[nameColumn])).ToList();
        var declared = Enum.GetValues<TEnum>().Where(v => Convert.ToInt32(v, CultureInfo.InvariantCulture) != 0)
            .Select(v => (Value: Convert.ToInt32(v, CultureInfo.InvariantCulture), Name: v.ToString())).ToList();

        Assert.Equal(specified.Count, specified.Select(s => s.Value).Distinct().Count());
        Assert.Equal(specified.OrderBy(s => s.Value), declared.OrderBy(d => d.Value));
    }
}

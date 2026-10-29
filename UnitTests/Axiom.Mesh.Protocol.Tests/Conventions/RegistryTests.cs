using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Axiom.Mesh.Protocol.Tests.Support;

namespace Axiom.Mesh.Protocol.Tests.Conventions;

/// <summary>
/// The registries of SPEC.md §14, LOCAL-SPEC.md §14.10 and PAIR-SPEC.md §11 that Protocol carries agree with the code in
/// both directions: a value added to one and not the other fails here.
/// </summary>
public sealed class RegistryTests
{
    // SPEC §14.1: ids 1 to 16, each once; MessageIds holds exactly these and the two ends of the extension range.
    [Fact]
    public void EveryMessageIdOfSpec14_1IsRegisteredOnce()
    {
        var specified = SpecMessageIds().ToDictionary(r => r.Name, r => r.Id, StringComparer.Ordinal);
        var declared = typeof(MessageIds).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.Name is not (nameof(MessageIds.FirstExtension) or nameof(MessageIds.LastExtension)))
            .ToDictionary(f => f.Name, f => (int)(ushort)f.GetRawConstantValue()!, StringComparer.Ordinal);

        Assert.Equal(specified.Count, specified.Values.Distinct().Count());
        Assert.Equal(specified.OrderBy(p => p.Key, StringComparer.Ordinal), declared.OrderBy(p => p.Key, StringComparer.Ordinal));
        Assert.Equal(17, (int)MessageIds.FirstExtension);
        Assert.Equal(255, (int)MessageIds.LastExtension);
    }

    // SPEC §14.1 and §15: every message of the registry has a class that carries its id, and the round-trip samples use
    // it, so no message escapes the codec checks.
    [Fact]
    public void EveryMessageIdInTheSpecHasARecordAndAppearsInTheRoundTripSamples()
    {
        Type[] messageTypes = [.. typeof(MeshMessage).Assembly.GetExportedTypes().Where(t => t.IsSubclassOf(typeof(MeshMessage)) && t != typeof(Extension))];
        var samples = MessageSamples.All().Where(m => m is not Extension).ToLookup(m => (int)m.Id);

        foreach ((string name, int id) in SpecMessageIds())
        {
            Type type = Assert.Single(messageTypes, t => t.Name == name || t.Name == name + "Message");
            Assert.True(samples.Contains(id), $"No round-trip sample of {name} ({id}).");
            Assert.All(samples[id], m => Assert.Equal(type, m.GetType()));
        }

        Assert.Equal(SpecMessageIds().Count(), messageTypes.Length);
    }

    // SPEC §14.2 and §14.3: every Bye reason and capability flag once, nothing the registry does not name.
    [Fact]
    public void EveryByeReasonAndCapabilityFlagOfSpec14IsDeclaredExactlyOnce()
    {
        var reasons = Rows("SPEC.md", "### 14.2 ").Select(r => (Value: int.Parse(r[0], CultureInfo.InvariantCulture), Name: r[1].Split(' ')[0])).ToList();
        Assert.Equal(reasons.OrderBy(r => r.Value), Enum.GetValues<ByeReason>().Select(r => ((int)r, r.ToString())).OrderBy(r => r.Item1));

        int[] bits = [.. Rows("SPEC.md", "### 14.3 ").Where(r => int.TryParse(r[0], out _)).Select(r => int.Parse(r[0], CultureInfo.InvariantCulture))];
        uint[] flags = [.. Enum.GetValues<Capabilities>().Where(c => c != Capabilities.None).Select(c => (uint)c)];
        Assert.Equal(bits.Select(b => 1u << b).Order(), flags.Order());
        Assert.Equal(Hello.KnownCapabilities, Enum.GetValues<Capabilities>().Aggregate(Capabilities.None, (all, c) => all | c));
    }

    // LOCAL §14.10: the pairing modes and states with their values; the pairing protocol shares them (PAIR-SPEC §11.3).
    [Fact]
    public void ThePairingModesAndStatesAreThoseOfLocalSpec14_10()
    {
        IReadOnlyList<IReadOnlyList<string[]>> tables = SectionTables.Of("LOCAL-SPEC.md", "### 14.10 ");
        AssertNamesValues<PairingMode>(tables[0], nameColumn: 0, valueColumn: 1);
        AssertNamesValues<PairingState>(tables[1], nameColumn: 0, valueColumn: 1);
    }

    // PAIR-SPEC §11.1 and §11.2: the pairing message ids and cancel reasons.
    [Fact]
    public void ThePairingMessageIdsAndCancelReasonsAreThoseOfPairSpec11()
    {
        var ids = Rows("PAIR-SPEC.md", "### 11.1 ").ToDictionary(r => r[1], r => int.Parse(r[0], CultureInfo.InvariantCulture), StringComparer.Ordinal);
        Assert.Equal(
            new Dictionary<string, int> { ["PairHello"] = PairProtocol.PairHelloId, ["PairConfirm"] = PairProtocol.PairConfirmId, ["PairCancel"] = PairProtocol.PairCancelId },
            ids);

        AssertNamesValues<PairCancelReason>(Rows("PAIR-SPEC.md", "### 11.2 "), nameColumn: 1, valueColumn: 0, includeZero: true);
    }

    private static IEnumerable<(string Name, int Id)> SpecMessageIds() =>
        Rows("SPEC.md", "### 14.1 ").Select(r => (r[1], int.Parse(r[0], CultureInfo.InvariantCulture)));

    private static IReadOnlyList<string[]> Rows(string document, string heading) => SectionTables.Of(document, heading)[0];

    private static void AssertNamesValues<TEnum>(IReadOnlyList<string[]> rows, int nameColumn, int valueColumn, bool includeZero = false)
        where TEnum : struct, Enum
    {
        var specified = rows.Select(r => (Value: int.Parse(r[valueColumn], CultureInfo.InvariantCulture), Name: r[nameColumn])).OrderBy(r => r.Value).ToList();
        var declared = Enum.GetValues<TEnum>()
            .Select(v => (Value: Convert.ToInt32(v, CultureInfo.InvariantCulture), Name: v.ToString()))
            .Where(v => includeZero || v.Value != 0)
            .OrderBy(v => v.Value)
            .ToList();

        Assert.Equal(specified.Count, specified.Select(s => s.Value).Distinct().Count());
        Assert.Equal(specified, declared);
    }
}

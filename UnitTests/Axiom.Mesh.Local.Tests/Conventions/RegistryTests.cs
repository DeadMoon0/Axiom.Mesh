using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Axiom.Mesh.Local.Tests.Support;

namespace Axiom.Mesh.Local.Tests.Conventions;

/// <summary>
/// The registries of LOCAL-SPEC.md §14 against the code, in both directions: a value added to the specification and not
/// to the code, or to the code and not to the specification, fails here.
/// </summary>
public sealed class RegistryTests
{
    [Fact]
    public void EveryRequestIdOfLocalSpec14_1HasAMessageAndASample()
    {
        var spec = SpecTables.Rows("### 14.1 ", 0)
            .Where(r => r[2].StartsWith("Request", StringComparison.Ordinal))
            .Select(r => (Id: ushort.Parse(r[0], CultureInfo.InvariantCulture), Name: r[1]))
            .ToList();
        var code = RequestTable.Default.All.Select(d => (d.Id, d.Name)).ToList();

        Assert.Equal(spec, code);
        foreach (RequestDescriptor request in RequestTable.Default.All)
        {
            Assert.True(MessageSamples.FromClient.ContainsKey("Request " + request.Name), $"{request.Name} has no request sample.");
            Assert.True(MessageSamples.FromHost.ContainsKey("Response " + request.Name), $"{request.Name} has no response sample.");
            Assert.Equal(request.Id, (ushort)request.RequestType.GetProperty("Id")!.GetValue(null)!);
            Assert.Equal(RowOf(request.Id)[3], request.Section);
        }

        // Every request class is in the table, and the table holds nothing else.
        Assert.Equal(
            Samples.RequestTypes().Select(t => t.FullName).Order(StringComparer.Ordinal),
            RequestTable.Default.All.Select(d => d.RequestType.FullName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void EveryEventIdOfLocalSpec14_1HasAnEventAndEveryEventIsInTheRegistry()
    {
        var spec = SpecTables.Rows("### 14.1 ", 0)
            .Where(r => r[2] == "Event")
            .Select(r => (Id: ushort.Parse(r[0], CultureInfo.InvariantCulture), Name: r[1]))
            .ToList();
        var code = Samples.EventTypes()
            .Select(t => (Id: (ushort)t.GetProperty("Id")!.GetValue(null)!, t.Name))
            .OrderBy(e => e.Id)
            .ToList();

        Assert.Equal(spec, code);
        Assert.Equal(spec.Select(e => e.Id), EventCatalog.Ids());
    }

    [Fact]
    public void TheSessionCallbackAndEnvelopeIdsOfLocalSpec14_1AreTheCodes()
    {
        var spec = SpecTables.Rows("### 14.1 ", 0)
            .Where(r => r[2] is "Session" or "Response" or "Reply" or "Callback" && !r[1].StartsWith('('))
            .ToDictionary(r => r[1], r => ushort.Parse(r[0], CultureInfo.InvariantCulture), StringComparer.Ordinal);

        Assert.Equal(LocalMessageIds.Hello, spec["Hello"]);
        Assert.Equal(LocalMessageIds.HelloAck, spec["HelloAck"]);
        Assert.Equal(LocalMessageIds.Bye, spec["Bye"]);
        Assert.Equal(LocalMessageIds.Ping, spec["Ping"]);
        Assert.Equal(LocalMessageIds.Pong, spec["Pong"]);
        Assert.Equal(LocalMessageIds.Drain, spec["Drain"]);
        Assert.Equal(LocalMessageIds.Response, spec["Response"]);
        Assert.Equal(LocalMessageIds.Reply, spec["Reply"]);
        Assert.Equal(spec["ForwardedCall"], ForwardedCall.Id);
        Assert.Equal(9, spec.Count);
    }

    [Theory]
    [InlineData("### 14.2 ", 0, typeof(Role))] // roles
    [InlineData("### 14.3 ", 0, typeof(ByeReason))] // Bye reasons
    [InlineData("### 14.5 ", 0, typeof(Topic))] // topics
    [InlineData("### 14.6 ", 0, typeof(OperationKind))] // operation kinds
    [InlineData("### 14.7 ", 0, typeof(OperationState))] // operation states
    [InlineData("### 14.8 ", 0, typeof(PinReason))] // pin reasons
    [InlineData("### 14.10 ", 0, typeof(PairingMode))] // pairing modes
    [InlineData("### 14.10 ", 1, typeof(PairingState))] // pairing states
    [InlineData("### 14.11 ", 0, typeof(PeerState))] // peer states
    [InlineData("### 14.11 ", 1, typeof(PeerChange))] // peer change kinds
    [InlineData("### 14.12 ", 0, typeof(PackageChange))] // Package change kinds
    [InlineData("### 11.1 ", 0, typeof(Status))] // status codes, the table §14.9 refers to
    public void EveryRegistryValueOfLocalSpec14IsAMemberOfItsEnumeration_AndNoMemberIsMissingFromTheRegistry(string heading, int table, Type enumeration)
    {
        // Some tables put the value first, some the name; a row whose value is a range ("21 to 32767") is a reservation.
        var spec = SpecTables.Rows(heading, table)
            .Select(r => IsValue(r[0]) ? (Value: r[0], Name: r[1]) : (Value: r[1], Name: r[0]))
            .Where(e => IsValue(e.Value))
            .Select(e => (Value: long.Parse(e.Value, CultureInfo.InvariantCulture), Name: Normalise(e.Name)))
            .OrderBy(e => e.Value)
            .ToList();

        // A member of value 0 that the registry leaves unassigned is the convention's explicit zero (every enum has one).
        bool registryHasZero = spec.Any(e => e.Value == 0);
        var code = Enum.GetValues(enumeration).Cast<object>()
            .Select(v => (Value: Convert.ToInt64(v, CultureInfo.InvariantCulture), Name: Normalise(Enum.GetName(enumeration, v)!)))
            .Where(e => e.Value != 0 || registryHasZero)
            .OrderBy(e => e.Value)
            .ToList();

        Assert.Equal(spec, code);
    }

    private static string[] RowOf(ushort id) =>
        SpecTables.Rows("### 14.1 ", 0).Single(r => r[0] == id.ToString(CultureInfo.InvariantCulture));

    private static bool IsValue(string cell) => int.TryParse(cell, NumberStyles.None, CultureInfo.InvariantCulture, out _);

    private static string Normalise(string name) => name.Trim('`').Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
}

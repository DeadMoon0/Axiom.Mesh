using System;
using Axiom.Mesh.Tests.Support;

namespace Axiom.Mesh.Protocol.Tests.Support;

/// <summary>Rosters and <c>PairHello</c>s of the test identities, for the pairing rules and the round trips.</summary>
internal static class PairingSamples
{
    /// <summary>Alice's Roster: her laptop, and her phone removed.</summary>
    public static VerifiedRoster AliceRoster { get; } = new RosterDraft
    {
        Seq = 4,
        Devices = [new RosterDevice(TestKeys.AliceLaptop.PublicKey, "laptop", 1, removed: false), new RosterDevice(TestKeys.AlicePhone.PublicKey, "phone", 1, removed: true)],
    }.Sign(TestKeys.Alice);

    /// <summary>Bob's Roster: his desktop.</summary>
    public static VerifiedRoster BobRoster { get; } = new RosterDraft
    {
        Seq = 1,
        Devices = [new RosterDevice(TestKeys.BobDesktop.PublicKey, "desktop", 1, removed: false)],
    }.Sign(TestKeys.Bob);

    /// <summary>The <c>PairHello</c> of Alice's laptop showing her code.</summary>
    public static PairHello AliceShows => new(PairingMode.ShowUser, TestKeys.AliceLaptop.PublicKey.AsDeviceId(), "Alice", TestKeys.Alice.PublicKey, AliceRoster.Bytes.Span);

    /// <summary>The <c>PairHello</c> of Bob's desktop entering Alice's code.</summary>
    public static PairHello BobJoins => new(PairingMode.JoinUser, TestKeys.BobDesktop.PublicKey.AsDeviceId(), "Bob", TestKeys.Bob.PublicKey, BobRoster.Bytes.Span);

    /// <summary>The <c>PairHello</c> of Alice's laptop enrolling a device.</summary>
    public static PairHello AliceAdds => new(PairingMode.AddDevice, TestKeys.AliceLaptop.PublicKey.AsDeviceId(), "Alice", TestKeys.Alice.PublicKey, AliceRoster.Bytes.Span);

    /// <summary>The <c>PairHello</c> of a new device, Eve's key standing in for it, showing its code.</summary>
    public static PairHello NewDeviceShows => new(PairingMode.ShowDevice, TestKeys.EveDevice.PublicKey.AsDeviceId(), "workshop-pc", null, ReadOnlySpan<byte>.Empty);
}

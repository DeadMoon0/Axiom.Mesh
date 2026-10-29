using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Discovery;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>
/// SPEC.md §3 and §4.4: a node dials only a not-removed device of a known user (its own user included) that it has no
/// session to and no backoff from; a static address is dialled and the handshake decides; an announcement of anybody
/// else costs nothing.
/// </summary>
public sealed class PeerTableTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly DateTimeOffset Now = TestIdentity.Epoch;
    private static readonly IPEndPoint Address = new(IPAddress.Parse("10.0.0.9"), 41810);

    // §3: Eve's device announces itself before Alice's phone does. Alice's laptop dials the phone, a device of its own user,
    // and never Eve's: Eve gets no entry in the peer table and no dial.
    [Fact]
    public async Task AnAnnouncementOfADeviceOfNoKnownUserIsNeverDialled()
    {
        await using TestMesh mesh = await TestMesh.StartAsync(
            TestMeshOptions.Of("alice-laptop", "eve-device", "alice-phone") with { Silent = ["eve-device", "alice-phone"] },
            None);
        MeshNode laptop = mesh["alice-laptop"].Node;

        mesh["eve-device"].Discovery.Announce();
        mesh["alice-phone"].Discovery.Announce();

        await mesh.WaitForFactAsync("alice-laptop", f => f.Kind == NodeFactKind.Dialling && f.Device == mesh["alice-phone"].Device, 0, "the laptop dials the phone", None);
        Assert.DoesNotContain(laptop.Facts.Recent(), f => f.Device == mesh["eve-device"].Device);
        Assert.DoesNotContain(laptop.Peers, p => p.Device == mesh["eve-device"].Device);
    }

    [Theory]
    [InlineData("bob-desktop", PeerState.Discovered, false, "None")] // a device of a known user, resting: dial
    [InlineData("alice-laptop", PeerState.Discovered, false, "Self")] // this node's own announcement
    [InlineData("eve-device", PeerState.Discovered, false, "UnknownDevice")] // no Roster the node holds lists it
    [InlineData("alice-phone", PeerState.Discovered, false, "Removed")] // its Roster removes it
    [InlineData("bob-desktop", PeerState.Established, false, "Connected")] // a session exists already
    [InlineData("bob-desktop", PeerState.Connected, false, "Connected")] // a session awaits its Hello
    [InlineData("bob-desktop", PeerState.Dialling, false, "Dialling")] // a dial runs
    [InlineData("bob-desktop", PeerState.BackedOff, true, "BackingOff")] // a backoff runs until after now
    [InlineData("bob-desktop", PeerState.BackedOff, false, "None")] // a backoff that has ended
    public void TheDialPolicyDialsOnlyANotRemovedDeviceOfAKnownUserWithoutSessionDialOrBackoff(string device, PeerState state, bool backingOff, string expected)
    {
        TestIdentity announced = TestIdentity.Of(device);
        var announcement = new Announcement(announced.DeviceId, announced.UserId, Address, DiscoverySource.Mdns);
        DateTimeOffset? until = backingOff ? Now.AddSeconds(1) : Now.AddSeconds(-1);

        DialDecision decision = DialPolicy.Decide(announcement, Trust.Instance, TestIdentity.Of("alice-laptop").DeviceId, state, until, Now);

        Assert.Equal(expected, decision.Reason.ToString());
    }

    // §3.3: a static address names no device; it is dialled unless a dial or session already runs or a backoff holds it.
    [Theory]
    [InlineData(PeerState.Discovered, false, "None")] // resting: dial and let the handshake decide
    [InlineData(PeerState.Dialling, false, "Dialling")] // a dial runs
    [InlineData(PeerState.Connected, false, "Connected")] // its device is connected
    [InlineData(PeerState.Discovered, true, "BackingOff")] // the address backs off
    public void AStaticAddressIsDialledUnlessItsDialOrSessionRunsOrItBacksOff(PeerState state, bool backingOff, string expected) =>
        Assert.Equal(expected, DialPolicy.DecideStatic(true, state, backingOff ? Now.AddSeconds(1) : null, Now).Reason.ToString());

    // A node without an identity has no Hello to send: it dials nobody.
    [Fact]
    public void ANodeWithoutAnIdentityDialsNobody()
    {
        TestIdentity bob = TestIdentity.Of("bob-desktop");
        var announcement = new Announcement(bob.DeviceId, bob.UserId, Address, DiscoverySource.Mdns);

        Assert.Equal(SkipReason.NoIdentity, DialPolicy.Decide(announcement, Trust.Instance, null, PeerState.Discovered, null, Now).Reason);
        Assert.Equal(SkipReason.NoIdentity, DialPolicy.DecideStatic(false, PeerState.Discovered, null, Now).Reason);
    }

    /// <summary>Alice's view: her laptop and Bob's desktop are current devices, her phone is removed, Eve is unknown.</summary>
    private sealed class Trust : ITrustSnapshotView
    {
        private readonly Dictionary<DeviceId, TrustedDevice> devices = new[]
        {
            ("alice-laptop", false),
            ("alice-phone", true),
            ("bob-desktop", false),
        }.Select(d => (Identity: TestIdentity.Of(d.Item1), Removed: d.Item2))
         .ToDictionary(d => d.Identity.DeviceId, d => new TrustedDevice(d.Identity.DeviceId, d.Identity.UserId, d.Identity.Device.PublicKey, d.Removed));

        public static Trust Instance { get; } = new();

        public bool TryGetDevice(DeviceId device, out TrustedDevice trusted) => devices.TryGetValue(device, out trusted);

        public bool TryGetManifest(PackageId package, [NotNullWhen(true)] out VerifiedManifest? manifest)
        {
            manifest = null;
            return false;
        }

        public bool TryGetGrant(PackageId package, UserId grantee, [NotNullWhen(true)] out VerifiedGrant? grant)
        {
            grant = null;
            return false;
        }
    }
}

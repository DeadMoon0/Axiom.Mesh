using System.Net.Security;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Protocol;

namespace Axiom.Mesh.Core.Tests.Regression;

/// <summary>What the TLS layer hands the session as the peer's identity, on both sides of one connection.</summary>
public sealed class PeerIdentityTests
{
    // Damage: the session handler, the duplicate rule and later replication read the peer's device, user, key and the
    // side that dialled from this feature; a field filled from the wrong source silently misattributes a peer.
    [Fact]
    public async Task TheIdentityFeatureNamesDeviceUserKeyAlpnRoleAndTheTls13Suite()
    {
        await using TestMesh mesh = await TestMesh.StartAsync(
            TestMeshOptions.Of("alice-laptop", "bob-desktop") with { Trust = TrustSeed.MutuallyKnown("alice-laptop", "bob-desktop"), Silent = ["alice-laptop"] },
            CancellationToken.None);
        await mesh.ConnectAsync("alice-laptop", "bob-desktop", CancellationToken.None);

        PeerIdentity bobAsAliceSees = mesh["alice-laptop"].Node.Table.EstablishedWith(mesh["bob-desktop"].Device)!.Peer;
        PeerIdentity aliceAsBobSees = mesh["bob-desktop"].Node.Table.EstablishedWith(mesh["alice-laptop"].Device)!.Peer;

        Assert.Equal(mesh["bob-desktop"].Device, bobAsAliceSees.Device);
        Assert.Equal(mesh["bob-desktop"].User, bobAsAliceSees.User);
        Assert.Equal(TestIdentity.Of("bob-desktop").Device.PublicKey, bobAsAliceSees.Key);
        Assert.Equal(Amp.Alpn, bobAsAliceSees.Alpn);
        Assert.False(bobAsAliceSees.IsPairing);
        Assert.Equal(SslProtocols.Tls13, bobAsAliceSees.Protocol);
        Assert.Contains(bobAsAliceSees.CipherSuite, new[] { TlsCipherSuite.TLS_AES_128_GCM_SHA256, TlsCipherSuite.TLS_AES_256_GCM_SHA384, TlsCipherSuite.TLS_CHACHA20_POLY1305_SHA256 });

        // ConnectAsync announces both, but Alice is silent at start and Bob dials only what he heard: whoever dialled,
        // the two sides of the kept connection see opposite roles.
        Assert.NotEqual(bobAsAliceSees.Role, aliceAsBobSees.Role);
        Assert.Equal(mesh["alice-laptop"].User, aliceAsBobSees.User);
    }
}

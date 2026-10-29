using System;
using System.Linq;
using Axiom.Mesh.Local.Tests.Support;

namespace Axiom.Mesh.Local.Tests.Rules;

/// <summary>The layout of <c>Hello</c> (LOCAL-SPEC.md §4.2): the token is its last 32 bytes, all zero unless a service presents one.</summary>
public sealed class HelloTests
{
    [Fact]
    public void HelloCarriesTheTokenAsItsLast32Bytes_AllZeroForAUser()
    {
        byte[] user = Frames.Payload(Frames.FromClient(new Hello { Role = Role.User, Name = "app" }));
        Assert.All(user[^32..], b => Assert.Equal(0, b));

        byte[] secret = [.. Enumerable.Range(1, 32).Select(i => (byte)i)];
        byte[] service = Frames.Payload(Frames.FromClient(new Hello { Role = Role.Service, Name = "cli", Token = ServiceToken.FromBytes(secret) }));
        Assert.Equal(secret, service[^32..]);

        // version u16, role u8, name, clientVersion, capabilities u32, token: nothing after the token.
        Assert.Equal(2 + 1 + (2 + 3) + 2 + 4 + 32, service.Length);
        Assert.Equal(new byte[] { 0, 1, 2, 0, 3, (byte)'c', (byte)'l', (byte)'i' }, service[..8]);
    }

    [Fact]
    public void ATokenParsesFromItsHexForm_ComparesInFixedTime_AndNeverPrintsItself()
    {
        const string hex = "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f";
        Assert.True(ServiceToken.TryParseHex(hex, out ServiceToken parsed));
        Assert.Equal(ServiceToken.FromBytes([.. Enumerable.Range(0, 32).Select(i => (byte)i)]), parsed);
        Assert.False(ServiceToken.TryParseHex(hex.AsSpan(0, 62), out _));
        Assert.True(parsed.FixedTimeEquals(parsed));
        Assert.False(parsed.FixedTimeEquals(ServiceToken.None));
        Assert.True(ServiceToken.None.IsNone);
        Assert.DoesNotContain("0a0b", parsed.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("0a0b", new Hello { Role = Role.Service, Name = "cli", Token = parsed }.ToString(), StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => ServiceToken.FromBytes(new byte[31]));
    }
}

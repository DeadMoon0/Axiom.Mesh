using System;
using System.Linq;
using Axiom.Mesh.Tests.Support;

namespace Axiom.Mesh.Protocol.Tests.Rules;

/// <summary>The pairing code (PAIR-SPEC.md §2).</summary>
public sealed class PairingCodeTests
{
    private static readonly string Key = Convert.ToBase64String(TestKeys.AliceLaptop.PublicKey.Encoded).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // PAIR-SPEC §2.1: amp1:<mode>:<host:port[,…]>:<device key base64url>; an IPv6 host in brackets (§2.2).
    [Fact]
    public void ACodeIsAmp1ModeAddressesAndTheBase64UrlDeviceKey_AndParsesBackToTheSameCode()
    {
        var code = new PairingCode(
            PairingMode.ShowUser,
            [new PairingAddress("192.168.1.20", 41810), new PairingAddress("fe80::1%eth0", 41810), new PairingAddress("alice-laptop.local", 5000)],
            TestKeys.AliceLaptop.PublicKey);

        Assert.Equal($"amp1:user:192.168.1.20:41810,[fe80::1%eth0]:41810,alice-laptop.local:5000:{Key}", code.Text);
        Assert.True(PairingCode.TryParse(code.Text, out PairingCode? parsed));
        Assert.Equal(PairingMode.ShowUser, parsed.Mode);
        Assert.Equal(PairingMode.JoinUser, parsed.EnteringMode);
        Assert.Equal(code.Addresses, parsed.Addresses);
        Assert.Equal(TestKeys.AliceLaptop.PublicKey, parsed.DeviceKey);

        Assert.True(PairingCode.TryParse($"amp1:device:10.0.0.7:41810:{Key}", out PairingCode? device));
        Assert.Equal(PairingMode.AddDevice, device.EnteringMode);
    }

    // PAIR-SPEC §2.3: a code is accepted only in exactly the form it is written.
    [Theory]
    [InlineData("amp2:user:10.0.0.7:41810:{key}")] // another prefix
    [InlineData("amp1:users:10.0.0.7:41810:{key}")] // another mode
    [InlineData("amp1:user::{key}")] // no address
    [InlineData("amp1:user:10.0.0.7:41810,:{key}")] // an empty address
    [InlineData("amp1:user:10.0.0.7:0:{key}")] // port 0
    [InlineData("amp1:user:10.0.0.7:65536:{key}")] // a port above 65535
    [InlineData("amp1:user:10.0.0.7:041810:{key}")] // a port with a leading zero
    [InlineData("amp1:user:fe80::1:41810:{key}")] // an IPv6 host without brackets
    [InlineData("amp1:user:my host:41810:{key}")] // a host with a space
    [InlineData("amp1:user:10.0.0.7:41810:{key}+")] // a key that is not base64url
    [InlineData("amp1:user:10.0.0.7:41810:{short}")] // a key one character short
    [InlineData("amp1:user:10.0.0.7:41810:{key}=")] // a padded key
    [InlineData("amp1:user:{nine}:{key}")] // nine addresses
    public void AMalformedCodeIsRefused(string template)
    {
        string text = template
            .Replace("{key}", Key, StringComparison.Ordinal)
            .Replace("{short}", Key[..^1], StringComparison.Ordinal)
            .Replace("{nine}", string.Join(",", Enumerable.Repeat("10.0.0.7:41810", 9)), StringComparison.Ordinal);

        Assert.False(PairingCode.TryParse(text, out _));
    }

    // LOCAL-SPEC §5.15: Pairing.code is a string(512); PAIR-SPEC §2.2: at most 8 addresses.
    [Fact]
    public void ACodeHoldsOneToEightAddressesAndAtMost512Characters()
    {
        PublicKey key = TestKeys.AliceLaptop.PublicKey;
        Assert.Throws<ArgumentException>(() => new PairingCode(PairingMode.ShowUser, [], key));
        Assert.Throws<ArgumentException>(() => new PairingCode(PairingMode.ShowUser, [.. Enumerable.Repeat(new PairingAddress("10.0.0.7", 1), 9)], key));
        Assert.Throws<ArgumentException>(() => new PairingCode(PairingMode.ShowUser, [.. Enumerable.Repeat(new PairingAddress(new string('h', 60), 41810), 8)], key));
        Assert.Throws<ArgumentException>(() => new PairingCode(PairingMode.JoinUser, [new PairingAddress("10.0.0.7", 1)], key));

        var longest = new PairingCode(PairingMode.ShowDevice, [.. Enumerable.Range(0, 8).Select(i => new PairingAddress($"host-{i}.example", 65535))], key);
        Assert.True(longest.Text.Length <= PairingCode.MaxLength);
        Assert.True(PairingCode.TryParse(longest.Text, out _));
    }
}

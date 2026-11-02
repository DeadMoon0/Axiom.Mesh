using System;
using System.Linq;
using Axiom.Mesh.Tests.Support;

namespace Axiom.Mesh.Protocol.Tests.Rules;

/// <summary>The target an entering side pins (PAIR-SPEC.md §2.4, §4.2): by a code's key, or by a discovered device id.</summary>
public sealed class PairingTargetTests
{
    private static readonly PairingAddress[] Addresses = [new PairingAddress("192.168.1.20", 41810), new PairingAddress("fe80::1%eth0", 41810)];

    // PAIR-SPEC §2.1, §4.2: a code pins its device key; the hash of that key is the device id, so the pin is the same
    // whichever way the device was named, and only that key completes the handshake.
    [Fact]
    public void ACodesTargetPinsTheHashOfItsKey_AndAdmitsExactlyThatKey()
    {
        var code = new PairingCode(PairingMode.ShowUser, Addresses, TestKeys.AliceLaptop.PublicKey);

        PairingTarget target = PairingTarget.OfCode(code);

        Assert.True(target.IsByCode);
        Assert.Same(code, target.Code);
        Assert.Equal(TestKeys.AliceLaptop.PublicKey.AsDeviceId(), target.Device);
        Assert.Equal(Addresses, target.Addresses);
        Assert.True(target.Admits(TestKeys.AliceLaptop.PublicKey));
        Assert.False(target.Admits(TestKeys.EveDevice.PublicKey));
        Assert.True(target.Admits(TestKeys.AliceLaptop.PublicKey.Encoded, out PublicKey? admitted));
        Assert.Equal(TestKeys.AliceLaptop.PublicKey, admitted);
        Assert.False(target.Admits(TestKeys.EveDevice.PublicKey.Encoded, out PublicKey? refused));
        Assert.Null(refused);
    }

    // PAIR-SPEC §2.4, §4.2: a device named by the id discovery announced admits only a key whose hash is that id; an
    // encoded key that is no P-256 point admits nothing.
    [Fact]
    public void ADiscoveredDevicesTargetAdmitsOnlyAKeyWhoseHashIsItsId()
    {
        PairingTarget target = PairingTarget.OfDevice(TestKeys.BobDesktop.PublicKey.AsDeviceId(), Addresses);

        Assert.False(target.IsByCode);
        Assert.Null(target.Code);
        Assert.Equal(Addresses, target.Addresses);
        Assert.True(target.Admits(TestKeys.BobDesktop.PublicKey));
        Assert.False(target.Admits(TestKeys.AliceLaptop.PublicKey));
        Assert.False(target.Admits(TestKeys.EveDevice.PublicKey));
        Assert.False(target.Admits(new byte[65], out _));
        Assert.False(target.Admits(ReadOnlySpan<byte>.Empty, out _));
        Assert.Contains(TestKeys.BobDesktop.PublicKey.AsDeviceId().ToString(), target.ToString(), StringComparison.Ordinal);
    }

    // PAIR-SPEC §2.2, §2.4: a target by id needs a device and 1 to 8 well-formed addresses.
    [Fact]
    public void ATargetByIdRefusesAZeroIdNoAddressTooManyAddressesAndAMalformedOne()
    {
        DeviceId bob = TestKeys.BobDesktop.PublicKey.AsDeviceId();

        Assert.Throws<ArgumentException>(() => PairingTarget.OfDevice(default, Addresses));
        Assert.Throws<ArgumentException>(() => PairingTarget.OfDevice(bob, []));
        Assert.Throws<ArgumentException>(() => PairingTarget.OfDevice(bob, [.. Enumerable.Repeat(Addresses[0], PairingCode.MaxAddresses + 1)]));
        Assert.Throws<ArgumentException>(() => PairingTarget.OfDevice(bob, [new PairingAddress("10.0.0.1", 0)]));
        Assert.Throws<ArgumentException>(() => PairingTarget.OfDevice(bob, [new PairingAddress("", 41810)]));
        Assert.NotNull(PairingTarget.OfDevice(bob, [.. Enumerable.Repeat(Addresses[0], PairingCode.MaxAddresses)]));
    }
}

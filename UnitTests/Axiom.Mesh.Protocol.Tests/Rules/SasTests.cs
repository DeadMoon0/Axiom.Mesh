using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Axiom.Mesh.Tests.Support;

namespace Axiom.Mesh.Protocol.Tests.Rules;

/// <summary>The short authentication string (PAIR-SPEC.md §8, DESIGN.md §5.2).</summary>
public sealed class SasTests
{
    // PAIR-SPEC §8: both sides derive the string from both user keys and both TLS keys, each pair sorted, so it is the
    // same on both screens; "a hostile LAN cannot substitute a key without the strings differing" (DESIGN.md §5.2).
    [Fact]
    public void TheSasIsEqualOnBothSidesAndDiffersWhenEitherKeyIsSubstituted()
    {
        PublicKey alice = TestKeys.Alice.PublicKey, laptop = TestKeys.AliceLaptop.PublicKey;
        PublicKey bob = TestKeys.Bob.PublicKey, desktop = TestKeys.BobDesktop.PublicKey;
        PublicKey eve = TestKeys.Eve.PublicKey, eveDevice = TestKeys.EveDevice.PublicKey;

        string onAlicesScreen = Sas.Derive(alice, laptop, bob, desktop);
        string onBobsScreen = Sas.Derive(bob, desktop, alice, laptop);
        Assert.Equal(onAlicesScreen, onBobsScreen);

        string[] substituted =
        [
            Sas.Derive(alice, laptop, eve, desktop),
            Sas.Derive(alice, laptop, bob, eveDevice),
            Sas.Derive(eve, laptop, bob, desktop),
            Sas.Derive(alice, eveDevice, bob, desktop),
            Sas.Derive(alice, laptop, eve, eveDevice),
        ];
        Assert.DoesNotContain(onAlicesScreen, substituted);

        // Enrolment: the device being enrolled has no user key; both sides still agree, and a substituted device differs.
        Assert.Equal(Sas.Derive(alice, laptop, null, desktop), Sas.Derive(null, desktop, alice, laptop));
        Assert.NotEqual(Sas.Derive(alice, laptop, null, desktop), Sas.Derive(alice, laptop, null, eveDevice));
    }

    // PAIR-SPEC §8: base32(H("axiom-mesh/pair/1" ‖ sort(userKeyA, userKeyB) ‖ sort(tlsKeyA, tlsKeyB)))[0..8], shown as
    // XXXX-XXXX. Computed here independently of the implementation.
    [Fact]
    public void TheSasIsTheFirst8Base32CharactersOfTheDomainSeparatedHashOfBothSortedPairs()
    {
        PublicKey[] users = [.. new[] { TestKeys.Alice.PublicKey, TestKeys.Bob.PublicKey }.OrderBy(k => Convert.ToHexString(k.Encoded), StringComparer.Ordinal)];
        PublicKey[] devices = [.. new[] { TestKeys.AliceLaptop.PublicKey, TestKeys.BobDesktop.PublicKey }.OrderBy(k => Convert.ToHexString(k.Encoded), StringComparer.Ordinal)];
        byte[] input = [.. Encoding.UTF8.GetBytes("axiom-mesh/pair/1"), .. users[0].Encoded, .. users[1].Encoded, .. devices[0].Encoded, .. devices[1].Encoded];
        byte[] digest = SHA256.HashData(input);
        ulong bits = digest.Take(5).Aggregate(0UL, (acc, b) => (acc << 8) | b);
        string base32 = new([.. Enumerable.Range(0, 8).Select(i => "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"[(int)((bits >> (35 - (5 * i))) & 31)])]);

        string sas = Sas.Derive(TestKeys.Alice.PublicKey, TestKeys.AliceLaptop.PublicKey, TestKeys.Bob.PublicKey, TestKeys.BobDesktop.PublicKey);

        Assert.Equal(base32[..4] + "-" + base32[4..], sas);
        Assert.Matches("^[A-Z2-7]{4}-[A-Z2-7]{4}$", sas);
    }
}

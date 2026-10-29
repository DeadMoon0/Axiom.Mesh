using System;
using System.Security.Cryptography;
using System.Text;
using Axiom.Mesh.Tests.Support;

namespace Axiom.Mesh.Tests.Rules;

/// <summary>SPEC.md §2.3 and §8.7: a Package id is H("axiom-mesh/package/1" ‖ owner user id ‖ nonce).</summary>
public sealed class PackageIdTests
{
    // §2.3 states the derivation; §8.7: "A Manifest whose ownerId or packageId does not match its key and nonce is
    // malformed." §15: "the Package id derivation of §8.7 accepted and refused".
    [Fact]
    public void APackageIdIsH_Domain_OwnerId_Nonce_AndAManifestWhoseIdDoesNotDeriveIsMalformed()
    {
        UserId owner = TestKeys.Alice.PublicKey.AsUserId();
        PackageNonce nonce = Samples.Nonce(7);
        byte[] input = [.. Encoding.UTF8.GetBytes("axiom-mesh/package/1"), .. Bytes(owner.Value), .. Bytes(nonce.Value)];
        Assert.Equal(new PackageId(new Hash32(SHA256.HashData(input))), PackageId.Derive(owner, nonce));

        VerifiedManifest manifest = Samples.Channel(nonce: 7);
        Assert.Equal(PackageId.Derive(owner, nonce), manifest.Content.Package);

        // The same Manifest with one bit of the packageId changed, and with one bit of the ownerId changed.
        byte[] wrongPackage = manifest.Bytes.ToArray();
        wrongPackage[2] ^= 1;
        Assert.Contains("packageId", Samples.Fault(wrongPackage).Detail, StringComparison.Ordinal);

        byte[] wrongOwner = manifest.Bytes.ToArray();
        wrongOwner[2 + 32] ^= 1;
        Assert.Contains("ownerId", Samples.Fault(wrongOwner).Detail, StringComparison.Ordinal);
    }

    // §8.7: "only the holder of ownerKey can produce a Manifest for a given Package id": Eve, signing with her own key
    // and Alice's nonce, gets another id, not Alice's.
    [Fact]
    public void AnotherKeyWithTheSameNonceDerivesAnotherPackage()
    {
        Assert.NotEqual(Samples.Channel(TestKeys.Alice, nonce: 3).Content.Package, Samples.Channel(TestKeys.Eve, nonce: 3).Content.Package);
    }

    private static byte[] Bytes(Hash32 value)
    {
        byte[] bytes = new byte[32];
        value.CopyTo(bytes);
        return bytes;
    }
}

using System;
using System.Numerics;
using System.Security.Cryptography;
using Axiom.Mesh.Tests.Support;

namespace Axiom.Mesh.Tests.Rules;

/// <summary>SPEC.md §2.2: ECDSA P-256 signatures in P1363 form, within 1 ≤ r &lt; n and 1 ≤ s ≤ (n − 1)/2.</summary>
public sealed class SignatureTests
{
    // The order n of P-256, from SEC 2 §2.4.2, which SPEC.md §2.2 names.
    private static readonly BigInteger N = BigInteger.Parse("0FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551", System.Globalization.NumberStyles.HexNumber);

    // §2.2: "1 ≤ s ≤ (n−1)/2 (low-s) … a signed object whose signature is outside these bounds is malformed. Signers
    // MUST produce low-s signatures." §15: "found malformed after s is replaced by n − s".
    [Fact]
    public void ASignatureWhoseSIsAboveHalfTheOrderIsMalformed_AndSignersAlwaysProduceLowS()
    {
        for (int i = 0; i < 64; i++)
        {
            Signature64 signature = TestKeys.AliceLaptop.Sign(Digests.Of(SigningDomain.Entry, BitConverter.GetBytes(i)));
            Assert.InRange(Big(signature.S), BigInteger.One, (N - 1) / 2);
        }

        VerifiedEntry entry = Samples.Message(TestKeys.AliceLaptop, Samples.Channel().Content.Package, 0, EntryParents.None);
        byte[] twin = entry.Bytes.ToArray();
        Span<byte> s = twin.AsSpan(twin.Length - 32);
        Bytes(N - Big(entry.Content.Signature.S)).CopyTo(s);

        // The high-s twin is a valid ECDSA signature over the same digest: only the low-s bound refuses it.
        using ECDsa verifier = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = TestKeys.AliceLaptop.PublicKey.Encoded[1..33].ToArray(), Y = TestKeys.AliceLaptop.PublicKey.Encoded[33..].ToArray() } });
        byte[] digest = new byte[32];
        Digests.Of(SigningDomain.Entry, twin.AsSpan(0, twin.Length - 64)).Value.CopyTo(digest);
        Assert.True(verifier.VerifyHash(digest, twin.AsSpan(twin.Length - 64), DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

        Assert.Equal(ObjectFaultKind.Malformed, Samples.Fault(twin).Kind);
        Assert.False(Signature64.TryParse(twin.AsSpan(twin.Length - 64), out _));
    }

    // §2.2: s at (n − 1)/2 is low-s and accepted, one above is not; r must be at least 1 and below n.
    [Theory]
    // r = 1 and s = (n − 1)/2: both at their bounds.
    [InlineData("1", "half", true)]
    // s one above (n − 1)/2.
    [InlineData("1", "half+1", false)]
    // s = 0.
    [InlineData("1", "0", false)]
    // r = 0.
    [InlineData("0", "1", false)]
    // r = n − 1, the largest r.
    [InlineData("n-1", "1", true)]
    // r = n.
    [InlineData("n", "1", false)]
    public void RAndSAreCheckedAgainstTheirBounds(string r, string s, bool accepted)
    {
        byte[] bytes = [.. Bytes(Value(r)), .. Bytes(Value(s))];
        Assert.Equal(accepted, Signature64.TryParse(bytes, out _));
    }

    // §2.2: "Signers MUST produce low-s signatures": a signer's high-s output is turned into its twin n − s.
    [Fact]
    public void NormalisingAHighSSignatureGivesItsLowSTwin()
    {
        byte[] high = [.. Bytes(BigInteger.One), .. Bytes(N - 5)];
        Signature64 low = Signature64.Normalize(high);
        Assert.Equal(new BigInteger(5), Big(low.S));
        Assert.Equal(BigInteger.One, Big(low.R));
    }

    private static BigInteger Value(string text) => text switch
    {
        "half" => (N - 1) / 2,
        "half+1" => ((N - 1) / 2) + 1,
        "n-1" => N - 1,
        "n" => N,
        _ => BigInteger.Parse(text, System.Globalization.CultureInfo.InvariantCulture),
    };

    private static BigInteger Big(Hash32 value)
    {
        byte[] bytes = new byte[32];
        value.CopyTo(bytes);
        return new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
    }

    private static byte[] Bytes(BigInteger value)
    {
        byte[] raw = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        byte[] padded = new byte[32];
        raw.CopyTo(padded, 32 - raw.Length);
        return padded;
    }
}

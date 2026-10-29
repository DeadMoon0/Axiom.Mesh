using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh;

/// <summary>
/// What a Grant's bytes say (SPEC.md §8.9), well-formed by construction: the grantee id derives from the grantee key and
/// the rights are defined. It carries no trust of its own; see <see cref="UnverifiedGrant"/> and <see cref="VerifiedGrant"/>.
/// </summary>
public sealed class GrantContent
{
    private GrantContent(ObjectHash hash, PackageId package, PublicKey granteeKey, GrantRights rights, ulong seq, Signature64 signature)
    {
        Hash = hash;
        Package = package;
        GranteeKey = granteeKey;
        Rights = rights;
        Seq = seq;
        Signature = signature;
    }

    /// <summary>The Grant's object hash.</summary>
    public ObjectHash Hash { get; }

    /// <summary>The Package the Grant is for.</summary>
    public PackageId Package { get; }

    /// <summary>The grantee, <c>H(GranteeKey)</c>.</summary>
    public UserId Grantee => GranteeKey.AsUserId();

    /// <summary>The grantee's user key, carried so every reader can verify the grantee's Roster (SPEC.md §13).</summary>
    public PublicKey GranteeKey { get; }

    /// <summary>What the grantee may do; <see cref="GrantRights.None"/> is a revocation.</summary>
    public GrantRights Rights { get; }

    /// <summary>The sequence number; the highest valid one per Package and grantee is current.</summary>
    public ulong Seq { get; }

    /// <summary>The owner's signature.</summary>
    public Signature64 Signature { get; }

    /// <summary>What decides whether this Grant is current (SPEC.md §8.9).</summary>
    public ObjectSeq Stamp => new(Seq, Hash);

    internal static GrantContent? Parse(HashedObject source, ref WireReader reader, out int signedLength, out ObjectFault fault)
    {
        PackageId package = PackageId.Read(ref reader);
        UserId grantee = UserId.Read(ref reader);
        PublicKey.TryRead(ref reader, out PublicKey? granteeKey);
        byte rights = reader.UInt8();
        ulong seq = reader.UInt64BigEndian();
        signedLength = reader.Position;
        if (!ObjectReading.SignatureAtEnd(ref reader, "Grant", out Signature64 signature, out fault))
            return null;

        if (grantee != granteeKey!.AsUserId())
        {
            ObjectReading.Fail("A Grant's granteeId is not H(granteeKey) (SPEC.md §8.9).", out fault);
            return null;
        }

        if (rights > (byte)GrantRights.Write)
        {
            ObjectReading.Fail($"A Grant's rights are {rights}; only 0, 1 and 2 are defined (SPEC.md §8.9).", out fault);
            return null;
        }

        return new GrantContent(source.Hash, package, granteeKey, (GrantRights)rights, seq, signature);
    }
}

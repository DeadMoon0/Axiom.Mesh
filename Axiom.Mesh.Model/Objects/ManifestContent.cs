using System;
using System.Collections.Generic;
using System.Text;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh;

/// <summary>
/// What a Manifest's bytes say (SPEC.md §8.7), well-formed by construction: the owner id derives from the owner key
/// and the Package id from the owner id and the nonce. It carries no trust of its own; see
/// <see cref="UnverifiedManifest"/> and <see cref="VerifiedManifest"/>.
/// </summary>
public sealed class ManifestContent
{
    private ManifestContent(ObjectHash hash, PackageId package, PublicKey ownerKey, PackageNonce nonce, PackageKind kind, ulong seq, string name, ulong created, Retention retention, Branch[] branches, Signature64 signature)
    {
        Hash = hash;
        Package = package;
        OwnerKey = ownerKey;
        Nonce = nonce;
        Kind = kind;
        Seq = seq;
        Name = name;
        Created = created;
        Retention = retention;
        Branches = branches;
        Signature = signature;
    }

    /// <summary>The Manifest's object hash.</summary>
    public ObjectHash Hash { get; }

    /// <summary>The Package, derived from <see cref="Owner"/> and <see cref="Nonce"/>.</summary>
    public PackageId Package { get; }

    /// <summary>The owner's user id, <c>H(OwnerKey)</c>.</summary>
    public UserId Owner => OwnerKey.AsUserId();

    /// <summary>The owner's user key, which signs the Manifest and the Package's Grants.</summary>
    public PublicKey OwnerKey { get; }

    /// <summary>The nonce the Package id derives from.</summary>
    public PackageNonce Nonce { get; }

    /// <summary>The Package kind; one <see cref="PackageKind"/> does not name is kept as its raw value.</summary>
    public PackageKind Kind { get; }

    /// <summary>The sequence number; the highest valid one is current.</summary>
    public ulong Seq { get; }

    /// <summary>The Package's name.</summary>
    public string Name { get; }

    /// <summary>Milliseconds since the Unix epoch when it was created; informational.</summary>
    public ulong Created { get; }

    /// <summary>The owner's retention hint; an undefined value reads as <see cref="Retention.Default"/>.</summary>
    public Retention Retention { get; }

    /// <summary>The branches, ascending by name as byte strings, names unique.</summary>
    public IReadOnlyList<Branch> Branches { get; }

    /// <summary>The owner's signature.</summary>
    public Signature64 Signature { get; }

    /// <summary>What decides whether this Manifest is current (SPEC.md §8.7).</summary>
    public ObjectSeq Stamp => new(Seq, Hash);

    internal static ManifestContent? Parse(HashedObject source, ref WireReader reader, out int signedLength, out ObjectFault fault)
    {
        signedLength = 0;
        PackageId package = PackageId.Read(ref reader);
        UserId owner = UserId.Read(ref reader);
        PublicKey.TryRead(ref reader, out PublicKey? ownerKey);
        PackageNonce nonce = PackageNonce.Read(ref reader);
        var kind = (PackageKind)reader.UInt16BigEndian();
        ulong seq = reader.UInt64BigEndian();
        string name = Wire.ReadString(ref reader, Limits.MaxManifestNameLength);
        ulong created = reader.UInt64BigEndian();
        byte retention = reader.UInt8();
        Branch[]? branches = ReadBranches(ref reader, out fault);
        if (branches is null)
            return null;

        signedLength = reader.Position;
        if (!ObjectReading.SignatureAtEnd(ref reader, "Manifest", out Signature64 signature, out fault))
            return null;

        if (owner != ownerKey!.AsUserId())
        {
            ObjectReading.Fail("A Manifest's ownerId is not H(ownerKey) (SPEC.md §8.7).", out fault);
            return null;
        }

        if (package != PackageId.Derive(owner, nonce))
        {
            ObjectReading.Fail("A Manifest's packageId is not H(\"axiom-mesh/package/1\" ‖ ownerId ‖ nonce) (SPEC.md §2.3, §8.7).", out fault);
            return null;
        }

        var readRetention = retention <= (byte)Retention.LatestOnly ? (Retention)retention : Retention.Default;
        return new ManifestContent(source.Hash, package, ownerKey, nonce, kind, seq, name, created, readRetention, branches, signature);
    }

    private static Branch[]? ReadBranches(ref WireReader reader, out ObjectFault fault)
    {
        fault = default;
        int count = Wire.ReadListCount(ref reader, Limits.MaxManifestBranches);
        var branches = new Branch[count];
        ReadOnlySpan<byte> previous = default;
        for (int i = 0; i < count && reader.Ok; i++)
        {
            ReadOnlyMemory<byte> name = Wire.ReadStringBytes(ref reader, Limits.MaxBranchNameLength);
            ObjectHash head = ObjectHash.Read(ref reader);
            if (reader.Ok && i > 0 && previous.SequenceCompareTo(name.Span) >= 0)
            {
                ObjectReading.Fail("A Manifest's branches are not sorted by name as byte strings, or a name repeats (SPEC.md §8.7).", out fault);
                return null;
            }

            branches[i] = new Branch(Encoding.UTF8.GetString(name.Span), head);
            previous = name.Span;
        }

        return branches;
    }
}

using System;
using System.Collections.Generic;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Protocol;

/// <summary>
/// What a node holds of one Package, as it lists it to a peer (SPEC.md §7.2): the current Manifest, the Grants the peer
/// may see (§9.7) and one Head per writing device.
/// </summary>
public sealed class PackageState
{
    private readonly ObjectHash[] grants;
    private readonly Head[] heads;

    /// <summary>A Package's state.</summary>
    /// <param name="package">The Package.</param>
    /// <param name="manifestHash">The hash of the current Manifest the sender holds (§8.7).</param>
    /// <param name="manifestSeq">That Manifest's <c>seq</c>.</param>
    /// <param name="grants">The current Grants the peer may see, strictly ascending; at most 256.</param>
    /// <param name="heads">One Head per writing device, strictly ascending by device id; at most 256.</param>
    /// <exception cref="ArgumentException">A list is too long or out of order.</exception>
    public PackageState(PackageId package, ObjectHash manifestHash, ulong manifestSeq, IReadOnlyList<ObjectHash> grants, IReadOnlyList<Head> heads)
        : this(package, manifestHash, manifestSeq, MessageFields.Own(grants, Limits.MaxPackageGrants, nameof(grants)), MessageFields.Own(heads, Limits.MaxPackageHeads, nameof(heads)))
    {
        if (!AreAscending(this.grants))
            throw new ArgumentException("The grants must be strictly ascending (SPEC.md §7.2).", nameof(grants));
        if (!AreAscending(this.heads))
            throw new ArgumentException("The heads must be strictly ascending by device id (SPEC.md §7.2).", nameof(heads));
    }

    private PackageState(PackageId package, ObjectHash manifestHash, ulong manifestSeq, ObjectHash[] grants, Head[] heads)
    {
        Package = package;
        ManifestHash = manifestHash;
        ManifestSeq = manifestSeq;
        this.grants = grants;
        this.heads = heads;
    }

    /// <summary>The Package.</summary>
    public PackageId Package { get; }

    /// <summary>The hash of the current Manifest the sender holds.</summary>
    public ObjectHash ManifestHash { get; }

    /// <summary>That Manifest's <c>seq</c>.</summary>
    public ulong ManifestSeq { get; }

    /// <summary>The current Grants the peer may see, ascending.</summary>
    public IReadOnlyList<ObjectHash> Grants => grants;

    /// <summary>One Head per writing device, ascending by device id.</summary>
    public IReadOnlyList<Head> Heads => heads;

    /// <summary>Reads a <c>PackageState</c>; a list over its bound or out of order fails the reader.</summary>
    /// <param name="reader">The reader.</param>
    /// <returns>The state; check <c>reader.Ok</c>.</returns>
    internal static PackageState Read(ref WireReader reader)
    {
        PackageId package = PackageId.Read(ref reader);
        ObjectHash manifestHash = ObjectHash.Read(ref reader);
        ulong manifestSeq = reader.UInt64BigEndian();
        ObjectHash[] grants = MessageFields.ReadHashes(ref reader, Limits.MaxPackageGrants);
        Head[] heads = ReadHeads(ref reader);
        if (reader.Ok && !(AreAscending(grants) && AreAscending(heads)))
            reader.Fail();
        return new PackageState(package, manifestHash, manifestSeq, grants, heads);
    }

    /// <summary>Writes the state.</summary>
    /// <param name="writer">The writer.</param>
    internal void Write(ref WireWriter writer)
    {
        Package.Write(ref writer);
        ManifestHash.Write(ref writer);
        writer.UInt64BigEndian(ManifestSeq);
        MessageFields.WriteHashes(ref writer, grants, Limits.MaxPackageGrants);
        Wire.WriteListCount(ref writer, heads.Length, Limits.MaxPackageHeads);
        foreach (Head head in heads)
        {
            head.Device.Write(ref writer);
            head.Entry.Write(ref writer);
            writer.UInt64BigEndian(head.Clock);
        }
    }

    /// <summary>The bytes of a <c>Head</c>: device id, entry hash, clock.</summary>
    internal const int HeadLength = Hash32.Length + Hash32.Length + sizeof(ulong);

    /// <summary>The fewest bytes of a <c>PackageState</c>: ids, seq and two empty lists.</summary>
    internal const int MinLength = Hash32.Length + Hash32.Length + sizeof(ulong) + sizeof(ushort) + sizeof(ushort);

    private static Head[] ReadHeads(ref WireReader reader)
    {
        int count = Wire.ReadListCount(ref reader, Limits.MaxPackageHeads);
        if (!reader.Ok || !MessageFields.Fits(ref reader, count, HeadLength))
            return [];

        var heads = new Head[count];
        for (int i = 0; i < count; i++)
            heads[i] = new Head(DeviceId.Read(ref reader), ObjectHash.Read(ref reader), reader.UInt64BigEndian());
        return reader.Ok ? heads : [];
    }

    private static bool AreAscending(ObjectHash[] hashes)
    {
        for (int i = 1; i < hashes.Length; i++)
        {
            if (hashes[i - 1] >= hashes[i])
                return false;
        }

        return true;
    }

    private static bool AreAscending(Head[] heads)
    {
        for (int i = 1; i < heads.Length; i++)
        {
            if (heads[i - 1].Device >= heads[i].Device)
                return false;
        }

        return true;
    }

    /// <inheritdoc />
    public override string ToString() => $"{Package}: manifest {ManifestSeq}, {grants.Length} grants, {heads.Length} heads";
}

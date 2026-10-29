using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>A Package this Host knows, Listed or Followed (LOCAL-SPEC.md §5.4).</summary>
public sealed record Package : IWireRecord<Package>
{
    /// <summary>The Package.</summary>
    public PackageId PackageId { get; init; }

    /// <summary>Listed (a peer lists it, not followed) or Followed.</summary>
    public PackageState State { get; init; }

    /// <summary>The owner; all zero while a Listed Package's Manifest is not held.</summary>
    public UserId OwnerId { get; init; }

    /// <summary>The Package kind (SPEC.md §14.8); unknown kinds are carried.</summary>
    public PackageKind Kind { get; init; }

    /// <summary>The name, from the current Manifest.</summary>
    public string Name { get; init; } = "";

    /// <summary>When it was created, from the current Manifest.</summary>
    public ulong Created { get; init; }

    /// <summary>The Manifest's retention.</summary>
    public Retention Retention { get; init; }

    /// <summary>The current Manifest's sequence number.</summary>
    public ulong ManifestSeq { get; init; }

    /// <summary>The current Manifest.</summary>
    public ObjectHash ManifestHash { get; init; }

    /// <summary>The Manifest's branches (SPEC.md §8.7), ascending by name.</summary>
    public IReadOnlyList<Branch> Branches { get; init; } = [];

    /// <summary>Established peers that currently list the Package to this node.</summary>
    public ushort ListedBy { get; init; }

    /// <summary>The local retention policy (§14.14).</summary>
    public LocalPolicy Policy { get; init; }

    /// <summary>This user's rights: Write for the owner, else the current Grant's, else none.</summary>
    public GrantRights Rights { get; init; }

    /// <summary>The Heads this Host has stored, ascending by device id.</summary>
    public IReadOnlyList<Head> Heads { get; init; } = [];

    /// <summary>Stored Entries of this Package.</summary>
    public ulong Entries { get; init; }

    /// <summary>The highest clock among them; 0 when none.</summary>
    public ulong LatestClock { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out Package? value)
    {
        var read = new Package
        {
            PackageId = PackageId.Read(ref reader),
            State = (PackageState)reader.UInt8(),
            OwnerId = UserId.Read(ref reader),
            Kind = (PackageKind)reader.UInt16BigEndian(),
            Name = Wire.ReadString(ref reader, FieldBounds.PackageNameLength),
            Created = reader.UInt64BigEndian(),
            Retention = (Retention)reader.UInt8(),
            ManifestSeq = reader.UInt64BigEndian(),
            ManifestHash = ObjectHash.Read(ref reader),
            Branches = LocalWire.ReadBranches(ref reader, FieldBounds.BranchesCount),
            ListedBy = reader.UInt16BigEndian(),
            Policy = (LocalPolicy)reader.UInt8(),
            Rights = (GrantRights)reader.UInt8(),
            Heads = LocalWire.ReadHeads(ref reader, FieldBounds.HeadsCount),
            Entries = reader.UInt64BigEndian(),
            LatestClock = reader.UInt64BigEndian(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        PackageId.Write(ref writer);
        writer.UInt8((byte)State);
        OwnerId.Write(ref writer);
        writer.UInt16BigEndian((ushort)Kind);
        Wire.WriteString(ref writer, Name, FieldBounds.PackageNameLength);
        writer.UInt64BigEndian(Created);
        writer.UInt8((byte)Retention);
        writer.UInt64BigEndian(ManifestSeq);
        ManifestHash.Write(ref writer);
        LocalWire.WriteBranches(ref writer, Branches, FieldBounds.BranchesCount);
        writer.UInt16BigEndian(ListedBy);
        writer.UInt8((byte)Policy);
        writer.UInt8((byte)Rights);
        LocalWire.WriteHeads(ref writer, Heads, FieldBounds.HeadsCount);
        writer.UInt64BigEndian(Entries);
        writer.UInt64BigEndian(LatestClock);
    }
}

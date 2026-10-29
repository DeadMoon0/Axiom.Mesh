using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>A current Grant (LOCAL-SPEC.md §5.5).</summary>
public sealed record Grant : IWireRecord<Grant>
{
    /// <summary>The Grant object.</summary>
    public ObjectHash Hash { get; init; }

    /// <summary>Its Package.</summary>
    public PackageId PackageId { get; init; }

    /// <summary>The grantee.</summary>
    public UserId GranteeId { get; init; }

    /// <summary>The rights (SPEC.md §8.9).</summary>
    public GrantRights Rights { get; init; }

    /// <summary>Its sequence number.</summary>
    public ulong Seq { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out Grant? value)
    {
        var read = new Grant
        {
            Hash = ObjectHash.Read(ref reader),
            PackageId = PackageId.Read(ref reader),
            GranteeId = UserId.Read(ref reader),
            Rights = (GrantRights)reader.UInt8(),
            Seq = reader.UInt64BigEndian(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        Hash.Write(ref writer);
        PackageId.Write(ref writer);
        GranteeId.Write(ref writer);
        writer.UInt8((byte)Rights);
        writer.UInt64BigEndian(Seq);
    }
}

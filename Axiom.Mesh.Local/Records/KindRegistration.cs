using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>The OK body of <c>RegisterKind</c> (LOCAL-SPEC.md §9.2).</summary>
public sealed record KindRegistration : IWireRecord<KindRegistration>
{
    /// <summary>The store epoch (§9.3): an index built under another epoch is discarded.</summary>
    public Hash32 StoreEpoch { get; init; }

    /// <summary>The store sequence of the newest stored Entry; 0 when none.</summary>
    public ulong StoreSeq { get; init; }

    /// <summary>Followed Packages of this kind.</summary>
    public uint Packages { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out KindRegistration? value)
    {
        var read = new KindRegistration
        {
            StoreEpoch = Hash32.Read(ref reader),
            StoreSeq = reader.UInt64BigEndian(),
            Packages = reader.UInt32BigEndian(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        StoreEpoch.Write(ref writer);
        writer.UInt64BigEndian(StoreSeq);
        writer.UInt32BigEndian(Packages);
    }
}

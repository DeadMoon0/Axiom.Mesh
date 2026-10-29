using System;
using System.Collections.Generic;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Protocol;

/// <summary>
/// <c>Have</c> (4, SPEC.md §7.4), Established only: "I hold these objects". A node never claims an object it does not
/// hold or that the peer may not see (§9.7).
/// </summary>
public sealed class Have : MeshMessage, IWireMessage<Have>
{
    private readonly ObjectHash[] hashes;

    /// <summary>A claim of held objects.</summary>
    /// <param name="hashes">The objects: at most 1 024.</param>
    /// <exception cref="ArgumentException">More than 1 024 hashes.</exception>
    public Have(IReadOnlyList<ObjectHash> hashes)
        : this(MessageFields.Own(hashes, Limits.MaxHaveHashes, nameof(hashes)))
    {
    }

    private Have(ObjectHash[] hashes) => this.hashes = hashes;

    /// <inheritdoc />
    public override ushort Id => MessageIds.Have;

    /// <summary>The objects the sender holds.</summary>
    public IReadOnlyList<ObjectHash> Hashes => hashes;

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, out Have message)
    {
        message = new Have(MessageFields.ReadHashes(ref reader, Limits.MaxHaveHashes));
        return reader.Ok;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer) => MessageFields.WriteHashes(ref writer, hashes, Limits.MaxHaveHashes);

    /// <inheritdoc />
    public override string ToString() => $"Have {hashes.Length}";
}

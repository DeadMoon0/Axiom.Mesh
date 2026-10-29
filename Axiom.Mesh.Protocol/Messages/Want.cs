using System;
using System.Collections.Generic;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Protocol;

/// <summary>
/// <c>Want</c> (5, SPEC.md §7.5), Established only: "send these if you have them". Each hash is answered with
/// <c>Object</c> or <c>NotFound</c> unless cancelled; <see cref="WantBudget"/> bounds what is outstanding to 64.
/// </summary>
public sealed class Want : MeshMessage, IWireMessage<Want>
{
    private readonly ObjectHash[] hashes;

    /// <summary>A request for objects.</summary>
    /// <param name="hashes">The objects: at most 64.</param>
    /// <exception cref="ArgumentException">More than 64 hashes.</exception>
    public Want(IReadOnlyList<ObjectHash> hashes)
        : this(MessageFields.Own(hashes, Limits.MaxWantHashes, nameof(hashes)))
    {
    }

    private Want(ObjectHash[] hashes) => this.hashes = hashes;

    /// <inheritdoc />
    public override ushort Id => MessageIds.Want;

    /// <summary>The objects wanted.</summary>
    public IReadOnlyList<ObjectHash> Hashes => hashes;

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, out Want message)
    {
        message = new Want(MessageFields.ReadHashes(ref reader, Limits.MaxWantHashes));
        return reader.Ok;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer) => MessageFields.WriteHashes(ref writer, hashes, Limits.MaxWantHashes);

    /// <inheritdoc />
    public override string ToString() => $"Want {hashes.Length}";
}

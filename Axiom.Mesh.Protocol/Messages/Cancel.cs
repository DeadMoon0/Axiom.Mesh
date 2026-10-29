using System;
using System.Collections.Generic;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Protocol;

/// <summary>
/// <c>Cancel</c> (8, SPEC.md §7.8), Established only: the sender no longer wants these objects; the receiver may drop
/// queued <c>Object</c>s for them and need not answer them.
/// </summary>
public sealed class Cancel : MeshMessage, IWireMessage<Cancel>
{
    private readonly ObjectHash[] hashes;

    /// <summary>A withdrawal of wants.</summary>
    /// <param name="hashes">The objects no longer wanted: at most 1 024.</param>
    /// <exception cref="ArgumentException">More than 1 024 hashes.</exception>
    public Cancel(IReadOnlyList<ObjectHash> hashes)
        : this(MessageFields.Own(hashes, Limits.MaxCancelHashes, nameof(hashes)))
    {
    }

    private Cancel(ObjectHash[] hashes) => this.hashes = hashes;

    /// <inheritdoc />
    public override ushort Id => MessageIds.Cancel;

    /// <summary>The objects no longer wanted.</summary>
    public IReadOnlyList<ObjectHash> Hashes => hashes;

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, out Cancel message)
    {
        message = new Cancel(MessageFields.ReadHashes(ref reader, Limits.MaxCancelHashes));
        return reader.Ok;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer) => MessageFields.WriteHashes(ref writer, hashes, Limits.MaxCancelHashes);

    /// <inheritdoc />
    public override string ToString() => $"Cancel {hashes.Length}";
}

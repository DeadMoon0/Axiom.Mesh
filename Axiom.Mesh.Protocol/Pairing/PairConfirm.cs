using System;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Protocol;

/// <summary>
/// <c>PairConfirm</c> (2, PAIR-SPEC.md §5.3): this side's user confirmed that both screens show the same short
/// authentication string. From the <see cref="PairingMode.AddDevice"/> side it carries the new Roster that enrols the
/// other device; from every other side it carries none.
/// </summary>
public sealed class PairConfirm : PairMessage, IWireMessage<PairConfirm>
{
    private readonly byte[] roster;

    private PairConfirm(byte[] roster) => this.roster = roster;

    /// <summary>A confirmation that carries nothing: sent by every mode but <see cref="PairingMode.AddDevice"/>.</summary>
    public static PairConfirm Plain => new([]);

    /// <summary>The new Roster that enrols the other device, sent by <see cref="PairingMode.AddDevice"/>; empty otherwise.</summary>
    public ReadOnlyMemory<byte> Roster => roster;

    /// <summary>The confirmation of the <see cref="PairingMode.AddDevice"/> side, carrying the Roster that enrols the device.</summary>
    /// <param name="roster">The new Roster, signed by the user key, listing the enrolled device.</param>
    /// <returns>The message.</returns>
    public static PairConfirm Enrolling(VerifiedRoster roster)
    {
        ArgumentNullException.ThrowIfNull(roster);
        return new PairConfirm(roster.Bytes.ToArray());
    }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, out PairConfirm message)
    {
        byte[] roster = Wire.ReadBlob(ref reader, Limits.MaxObjectLength).ToArray();
        message = new PairConfirm(reader.Ok ? roster : []);
        return reader.Ok;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer) => Wire.WriteBlob(ref writer, roster, Limits.MaxObjectLength);

    /// <inheritdoc />
    public override string ToString() => roster.Length == 0 ? "PairConfirm" : $"PairConfirm with a Roster of {roster.Length} bytes";
}

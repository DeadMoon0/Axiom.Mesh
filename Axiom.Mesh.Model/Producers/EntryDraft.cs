using System;
using System.Buffers;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh;

/// <summary>
/// An Entry to write (SPEC.md §8.6): <see cref="Sign"/> encodes it, has the writing device sign it, and parses and
/// verifies the result, so the encoder is proven on every signature.
/// </summary>
public sealed class EntryDraft
{
    /// <summary>The Package written to.</summary>
    public required PackageId Package { get; init; }

    /// <summary>The Lamport clock; with <see cref="Parents"/>, usually what <see cref="ParentRule.Choose"/> chose.</summary>
    public required ulong Clock { get; init; }

    /// <summary>The parents; empty only at clock 0.</summary>
    public required EntryParents Parents { get; init; }

    /// <summary>The payload; its kind is the Entry's kind.</summary>
    public required EntryPayload Payload { get; init; }

    /// <summary>Encodes and signs the Entry with the writing device's key.</summary>
    /// <param name="device">The writing device's signer; the Entry's device id is its key's id.</param>
    /// <returns>The Entry, verified; still to be admitted against the node's trust before it is stored.</returns>
    /// <exception cref="ArgumentException">The clock and parents are not well-formed, or the payload breaks a bound.</exception>
    public VerifiedEntry Sign(ISigner device)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (!Lamport.IsWellFormed(Clock, Parents))
            throw new ArgumentException($"Clock {Clock} with {Parents.Count} parents is not well-formed: the clock must be below 2^63 and only clock 0 may have no parents (SPEC.md §8.6).", nameof(Clock));

        byte[] payload = PayloadEncoder.Encode(Payload);
        ArrayBufferWriter<byte> output = ObjectEncoding.Start(ObjectTag.Entry);
        var writer = new WireWriter(output);
        Package.Write(ref writer);
        device.PublicKey.AsDeviceId().Write(ref writer);
        writer.UInt64BigEndian(Clock);
        Parents.Write(ref writer);
        writer.UInt16BigEndian((ushort)Payload.Kind);
        Wire.WriteBlob(ref writer, payload, Limits.MaxPayloadLength);

        UnverifiedEntry entry = ObjectEncoding.ParseOwn<UnverifiedEntry>(ObjectEncoding.Sign(output, SigningDomain.Entry, device));
        return VerifiedEntry.TryVerify(entry, device.PublicKey, out VerifiedEntry? verified)
            ? verified
            : throw ObjectEncoding.SignerDefect("Entry");
    }
}

using System;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh;

/// <summary>
/// What an Entry's bytes say (SPEC.md §8.6), well-formed by construction: its envelope. The payload is opaque here, of
/// any kind; what the node acts on is in the envelope, <see cref="References"/> and <see cref="Redacts"/>. It carries no
/// trust of its own: whether it is believed is said by the object that holds it, <see cref="UnverifiedEntry"/>,
/// <see cref="VerifiedEntry"/> or <see cref="AdmittedEntry"/>.
/// </summary>
public sealed class EntryContent
{
    private EntryContent(ObjectHash hash, PackageId package, DeviceId device, ulong clock, EntryParents parents, EntryKind kind, EntryReferences references, ObjectHash redacts, ReadOnlyMemory<byte> payload, Signature64 signature)
    {
        Hash = hash;
        Package = package;
        Device = device;
        Clock = clock;
        Parents = parents;
        Kind = kind;
        References = references;
        Redacts = redacts;
        Payload = payload;
        Signature = signature;
    }

    /// <summary>The Entry's object hash.</summary>
    public ObjectHash Hash { get; }

    /// <summary>The Package the Entry belongs to.</summary>
    public PackageId Package { get; }

    /// <summary>The writing device.</summary>
    public DeviceId Device { get; }

    /// <summary>The Lamport clock, below 2^63 (SPEC.md §9.3).</summary>
    public ulong Clock { get; }

    /// <summary>The Entries this one follows; empty only at clock 0.</summary>
    public EntryParents Parents { get; }

    /// <summary>The Entry kind, service-defined (SPEC.md §14.7); a kind <see cref="EntryKind"/> does not name is kept as its raw value.</summary>
    public EntryKind Kind { get; }

    /// <summary>The objects the Entry needs, ascending (SPEC.md §8.6).</summary>
    public EntryReferences References { get; }

    /// <summary>The Entry this one withdraws (SPEC.md §9.4), or the zero hash when it withdraws none.</summary>
    public ObjectHash Redacts { get; }

    /// <summary>Whether the Entry is a redaction (a non-zero <see cref="Redacts"/>), whatever its kind.</summary>
    public bool IsRedaction => !Redacts.IsZero;

    /// <summary>The payload bytes as in the object, opaque to the node (SPEC.md §8.5).</summary>
    public ReadOnlyMemory<byte> Payload { get; }

    /// <summary>The writing device's signature.</summary>
    public Signature64 Signature { get; }

    /// <summary>The Entry's position in the total order (SPEC.md §9.3).</summary>
    public EntryKey Key => new(Clock, Device, Hash);

    /// <summary>What the parent and redaction rules read of the Entry.</summary>
    public EntryFacts Facts => new(Package, Redacts, Key);

    /// <summary>What the writer said, apart from where the Entry sits in history.</summary>
    public EntryBody Body => new(Kind, Payload, References, Redacts);

    internal static EntryContent? Parse(HashedObject source, ref WireReader reader, out int signedLength, out ObjectFault fault)
    {
        PackageId package = PackageId.Read(ref reader);
        DeviceId device = DeviceId.Read(ref reader);
        ulong clock = reader.UInt64BigEndian();
        EntryParents parents = EntryParents.Read(ref reader);
        var kind = (EntryKind)reader.UInt16BigEndian();
        EntryReferences references = EntryReferences.Read(ref reader);
        ObjectHash redacts = ObjectHash.Read(ref reader);
        ReadOnlyMemory<byte> payload = Wire.ReadBlob(ref reader, Limits.MaxPayloadLength);
        signedLength = reader.Position;
        if (!ObjectReading.SignatureAtEnd(ref reader, "Entry", out Signature64 signature, out fault))
            return null;

        if (!Lamport.IsWellFormed(clock, parents))
        {
            ObjectReading.Fail("An Entry's clock is 2^63 or more, or it names no parents above clock 0 (SPEC.md §8.6).", out fault);
            return null;
        }

        return new EntryContent(source.Hash, package, device, clock, parents, kind, references, redacts, payload, signature);
    }
}

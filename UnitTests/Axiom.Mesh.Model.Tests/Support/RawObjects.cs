using System;
using System.Buffers;
using System.Collections.Generic;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Tests.Support;

/// <summary>
/// Writes objects field by field exactly as told, bypassing every check the producers make, so tests can build what a
/// faulty peer would send.
/// </summary>
internal static class RawObjects
{
    /// <summary>A signature within the bounds of SPEC.md §2.2 that verifies against no key: r = s = 1.</summary>
    public static byte[] FakeSignature
    {
        get
        {
            var bytes = new byte[Signature64.Length];
            bytes[31] = 1;
            bytes[63] = 1;
            return bytes;
        }
    }

    /// <summary>An object of the given tag and version with the given body.</summary>
    public static byte[] Unsigned(byte tag, Action<IBufferWriter<byte>> body, byte version = 1)
    {
        var output = new ArrayBufferWriter<byte>();
        output.Write(new byte[] { tag, version });
        body(output);
        return output.WrittenSpan.ToArray();
    }

    /// <summary>An object signed by <paramref name="signer"/> over everything before the signature, as SPEC.md §2.2 says.</summary>
    public static byte[] Signed(ObjectTag tag, SigningDomain domain, ISigner signer, Action<IBufferWriter<byte>> body)
    {
        var output = new ArrayBufferWriter<byte>();
        output.Write(new byte[] { (byte)tag, 1 });
        body(output);
        var writer = new WireWriter(output);
        signer.Sign(Digests.Of(domain, output.WrittenSpan)).Write(ref writer);
        return output.WrittenSpan.ToArray();
    }

    /// <summary>An object ending in <see cref="FakeSignature"/>, for checks decided before any signature is.</summary>
    public static byte[] FakeSigned(ObjectTag tag, Action<IBufferWriter<byte>> body) =>
        Unsigned((byte)tag, output =>
        {
            body(output);
            output.Write(FakeSignature);
        });

    /// <summary>An Entry body with every field as given, parents in the given order, no references and no <c>redacts</c>.</summary>
    public static Action<IBufferWriter<byte>> EntryBody(PackageId package, DeviceId device, ulong clock, IReadOnlyList<ObjectHash> parents, ushort kind, ReadOnlySpan<byte> payload) =>
        EntryBody(package, device, clock, parents, kind, [], default, payload);

    /// <summary>An Entry body with every field as given, parents and references in the given order.</summary>
    public static Action<IBufferWriter<byte>> EntryBody(PackageId package, DeviceId device, ulong clock, IReadOnlyList<ObjectHash> parents, ushort kind, IReadOnlyList<ObjectHash> references, ObjectHash redacts, ReadOnlySpan<byte> payload)
    {
        byte[] payloadCopy = payload.ToArray();
        ObjectHash[] referencesCopy = [.. references];
        return output =>
        {
            var writer = new WireWriter(output);
            package.Write(ref writer);
            device.Write(ref writer);
            writer.UInt64BigEndian(clock);
            writer.UInt16BigEndian((ushort)parents.Count);
            foreach (ObjectHash parent in parents)
                parent.Write(ref writer);
            writer.UInt16BigEndian(kind);
            writer.UInt16BigEndian((ushort)referencesCopy.Length);
            foreach (ObjectHash reference in referencesCopy)
                reference.Write(ref writer);
            redacts.Write(ref writer);
            writer.UInt32BigEndian((uint)payloadCopy.Length);
            writer.Bytes(payloadCopy);
        };
    }

    /// <summary>A string(max) written without any check: the length as given, then the bytes.</summary>
    public static void String(ref WireWriter writer, ReadOnlySpan<byte> utf8)
    {
        writer.UInt16BigEndian((ushort)utf8.Length);
        writer.Bytes(utf8);
    }

    /// <summary>A hash of 32 equal bytes.</summary>
    public static ObjectHash Hash(byte fill)
    {
        Span<byte> bytes = stackalloc byte[Hash32.Length];
        bytes.Fill(fill);
        return new ObjectHash(new Hash32(bytes));
    }
}

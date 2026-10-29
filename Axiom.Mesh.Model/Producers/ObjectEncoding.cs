using System;
using System.Buffers;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh;

/// <summary>The steps every producer shares: start an object, sign it, and prove the result parses as what was meant.</summary>
internal static class ObjectEncoding
{
    /// <summary>A buffer holding an object's tag and version (SPEC.md §8.1).</summary>
    /// <param name="tag">The object's tag.</param>
    /// <returns>The buffer; write the body after it.</returns>
    public static ArrayBufferWriter<byte> Start(ObjectTag tag)
    {
        var output = new ArrayBufferWriter<byte>();
        var writer = new WireWriter(output);
        writer.UInt8((byte)tag);
        writer.UInt8(Objects.Version);
        return output;
    }

    /// <summary>Appends the signature over everything written so far (SPEC.md §2.2).</summary>
    /// <param name="output">The object up to its signature.</param>
    /// <param name="domain">The object's signing domain.</param>
    /// <param name="signer">The key holder.</param>
    /// <returns>The object's bytes.</returns>
    public static byte[] Sign(ArrayBufferWriter<byte> output, SigningDomain domain, ISigner signer)
    {
        Signature64 signature = signer.Sign(Digests.Of(domain, output.WrittenSpan));
        var writer = new WireWriter(output);
        signature.Write(ref writer);
        return Finish(output);
    }

    /// <summary>The object's bytes, refused when longer than an object may be.</summary>
    /// <param name="output">The whole object.</param>
    /// <returns>The bytes.</returns>
    public static byte[] Finish(ArrayBufferWriter<byte> output)
    {
        if (output.WrittenCount > Limits.MaxObjectLength)
            throw new ArgumentException($"The object would be {output.WrittenCount} bytes; at most {Limits.MaxObjectLength} are allowed (SPEC.md §8.1).");
        return output.WrittenSpan.ToArray();
    }

    /// <summary>Parses bytes this side produced; anything but a <typeparamref name="T"/> is an encoder defect.</summary>
    /// <typeparam name="T">The parsed type the producer meant.</typeparam>
    /// <param name="bytes">The object's bytes.</param>
    /// <returns>The parsed object.</returns>
    public static T ParseOwn<T>(byte[] bytes)
        where T : ParsedObject
    {
        if (Objects.TryParse(HashedObject.Of(bytes), out ParsedObject? parsed, out ObjectFault fault) && parsed is T own)
            return own;
        throw new InvalidOperationException($"Axiom.Mesh encoded a {typeof(T).Name} that does not parse back ({fault}); this is a defect in Axiom.Mesh.");
    }

    /// <summary>The failure of a signature that does not verify against the signer's own public key.</summary>
    /// <param name="what">The object.</param>
    /// <returns>The exception to throw.</returns>
    public static InvalidOperationException SignerDefect(string what) =>
        new($"The {what} signed by the given ISigner does not verify against the signer's own PublicKey; the signer's key and its PublicKey do not belong together.");
}

using System.Buffers;
using Axiom.Mesh.Local;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Core;

/// <summary>A record of LOCAL-SPEC.md as the bytes it is encoded to: an operation's result body (§8.6) is one.</summary>
internal static class RecordBytes
{
    /// <summary>The encoded record.</summary>
    /// <typeparam name="T">The record's type.</typeparam>
    /// <param name="record">The record.</param>
    /// <returns>Its bytes.</returns>
    public static byte[] Of<T>(T record)
        where T : class, IWireRecord<T>
    {
        var output = new ArrayBufferWriter<byte>();
        var writer = new WireWriter(output);
        record.Write(ref writer);
        return output.WrittenSpan.ToArray();
    }
}

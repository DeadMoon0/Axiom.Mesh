using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh;

/// <summary>Parses objects (SPEC.md §8): the step from <see cref="HashedObject"/> to <see cref="ParsedObject"/>.</summary>
public static class Objects
{
    /// <summary>The <c>version</c> byte of every AMP/1 object (SPEC.md §1.4, §8.1).</summary>
    public const byte Version = 1;

    /// <summary>
    /// Parses an object by its tag. The result is one of <see cref="Chunk"/>, <see cref="ChunkList"/>, <see cref="Tree"/>,
    /// <see cref="UnverifiedEntry"/>, <see cref="UnverifiedManifest"/>, <see cref="UnverifiedRoster"/>,
    /// <see cref="UnverifiedGrant"/>, or <see cref="OpaqueObject"/> for a tag this implementation does not know (§1.4).
    /// Never throws: whatever the bytes, the outcome is a parsed object or a malformed fault.
    /// </summary>
    /// <param name="source">The bytes with their verified hash.</param>
    /// <param name="parsed">The object, or <see langword="null"/>.</param>
    /// <param name="fault">Why the object is malformed, when it is.</param>
    /// <returns><see langword="true"/> when the object is well-formed.</returns>
    public static bool TryParse(HashedObject source, [NotNullWhen(true)] out ParsedObject? parsed, out ObjectFault fault)
    {
        parsed = null;
        if (source.Bytes.Length > Limits.MaxObjectLength)
            return ObjectReading.Fail($"An object is at most {Limits.MaxObjectLength} bytes; this one is {source.Bytes.Length} (SPEC.md §8.1).", out fault);
        if (source.Bytes.Length < 2)
            return ObjectReading.Fail("An object is shorter than its tag and version (SPEC.md §8.1).", out fault);

        var reader = new WireReader(source.Bytes);
        byte tag = reader.UInt8();
        if (reader.UInt8() != Version)
            return ObjectReading.Fail($"An object's version is not {Version} (SPEC.md §8.1).", out fault);

        fault = default;
        parsed = (ObjectTag)tag switch
        {
            ObjectTag.Chunk => Chunk.Parse(source, ref reader, out fault),
            ObjectTag.ChunkList => ChunkList.Parse(source, ref reader, out fault),
            ObjectTag.Tree => Tree.Parse(source, ref reader, out fault),
            ObjectTag.Entry => UnverifiedEntry.Parse(source, ref reader, out fault),
            ObjectTag.Manifest => UnverifiedManifest.Parse(source, ref reader, out fault),
            ObjectTag.Roster => UnverifiedRoster.Parse(source, ref reader, out fault),
            ObjectTag.Grant => UnverifiedGrant.Parse(source, ref reader, out fault),
            _ => OpaqueObject.Parse(source, tag),
        };
        return parsed is not null;
    }
}

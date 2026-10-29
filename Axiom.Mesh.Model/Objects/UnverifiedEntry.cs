using System;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh;

/// <summary>
/// A well-formed Entry whose signature has not been checked (SPEC.md §8.6): it may be held until its writer's key is
/// known, and must not be stored, served, forwarded or acted on before <see cref="VerifiedEntry.TryVerify"/>.
/// </summary>
public sealed class UnverifiedEntry : ParsedObject
{
    private readonly int signedLength;

    private UnverifiedEntry(HashedObject source, EntryContent content, int signedLength)
        : base(source.Hash, source.Bytes)
    {
        Content = content;
        this.signedLength = signedLength;
    }

    /// <summary>What the bytes say, not yet believed.</summary>
    public EntryContent Content { get; }

    /// <inheritdoc />
    public override ObjectTag Tag => ObjectTag.Entry;

    /// <summary>The bytes the signature covers: everything before it (SPEC.md §2.2).</summary>
    internal ReadOnlySpan<byte> SignedBytes => Bytes.Span[..signedLength];

    internal static UnverifiedEntry? Parse(HashedObject source, ref WireReader reader, out ObjectFault fault)
    {
        EntryContent? content = EntryContent.Parse(source, ref reader, out int signedLength, out fault);
        return content is null ? null : new UnverifiedEntry(source, content, signedLength);
    }
}

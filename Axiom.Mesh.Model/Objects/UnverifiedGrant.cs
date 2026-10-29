using System;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh;

/// <summary>
/// A well-formed Grant whose signature has not been checked (SPEC.md §8.9). It verifies against the owner key of the
/// current Manifest of its Package, which the node must hold first; until then it is unverifiable.
/// </summary>
public sealed class UnverifiedGrant : ParsedObject
{
    private readonly int signedLength;

    private UnverifiedGrant(HashedObject source, GrantContent content, int signedLength)
        : base(source.Hash, source.Bytes)
    {
        Content = content;
        this.signedLength = signedLength;
    }

    /// <summary>What the bytes say, not yet believed.</summary>
    public GrantContent Content { get; }

    /// <inheritdoc />
    public override ObjectTag Tag => ObjectTag.Grant;

    internal ReadOnlySpan<byte> SignedBytes => Bytes.Span[..signedLength];

    internal static UnverifiedGrant? Parse(HashedObject source, ref WireReader reader, out ObjectFault fault)
    {
        GrantContent? content = GrantContent.Parse(source, ref reader, out int signedLength, out fault);
        return content is null ? null : new UnverifiedGrant(source, content, signedLength);
    }
}

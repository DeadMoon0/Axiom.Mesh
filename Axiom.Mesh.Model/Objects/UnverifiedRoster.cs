using System;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh;

/// <summary>
/// A well-formed Roster whose signature has not been checked (SPEC.md §8.8). It verifies against its own user key,
/// so <see cref="VerifiedRoster.TryVerify"/> needs nothing else.
/// </summary>
public sealed class UnverifiedRoster : ParsedObject
{
    private readonly int signedLength;

    private UnverifiedRoster(HashedObject source, RosterContent content, int signedLength)
        : base(source.Hash, source.Bytes)
    {
        Content = content;
        this.signedLength = signedLength;
    }

    /// <summary>What the bytes say, not yet believed.</summary>
    public RosterContent Content { get; }

    /// <inheritdoc />
    public override ObjectTag Tag => ObjectTag.Roster;

    internal ReadOnlySpan<byte> SignedBytes => Bytes.Span[..signedLength];

    internal static UnverifiedRoster? Parse(HashedObject source, ref WireReader reader, out ObjectFault fault)
    {
        RosterContent? content = RosterContent.Parse(source, ref reader, out int signedLength, out fault);
        return content is null ? null : new UnverifiedRoster(source, content, signedLength);
    }
}

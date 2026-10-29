using System;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh;

/// <summary>
/// A well-formed Manifest whose signature has not been checked (SPEC.md §8.7). It verifies against its own owner key,
/// so <see cref="VerifiedManifest.TryVerify"/> needs nothing else.
/// </summary>
public sealed class UnverifiedManifest : ParsedObject
{
    private readonly int signedLength;

    private UnverifiedManifest(HashedObject source, ManifestContent content, int signedLength)
        : base(source.Hash, source.Bytes)
    {
        Content = content;
        this.signedLength = signedLength;
    }

    /// <summary>What the bytes say, not yet believed.</summary>
    public ManifestContent Content { get; }

    /// <inheritdoc />
    public override ObjectTag Tag => ObjectTag.Manifest;

    internal ReadOnlySpan<byte> SignedBytes => Bytes.Span[..signedLength];

    internal static UnverifiedManifest? Parse(HashedObject source, ref WireReader reader, out ObjectFault fault)
    {
        ManifestContent? content = ManifestContent.Parse(source, ref reader, out int signedLength, out fault);
        return content is null ? null : new UnverifiedManifest(source, content, signedLength);
    }
}

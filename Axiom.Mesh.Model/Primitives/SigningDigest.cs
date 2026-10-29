namespace Axiom.Mesh;

/// <summary>
/// What a signature covers: <c>H(domain ‖ body)</c> (SPEC.md §2.2). Made only by <see cref="Digests.Of"/>, so a signer
/// can be handed nothing but a domain-separated digest of bytes as they are on the wire.
/// </summary>
/// <remarks><c>default</c> is the all-zero digest, which no producer signs.</remarks>
[ClosedShape("A digest is one SHA-256 output (SPEC.md §2.2).")]
public readonly record struct SigningDigest
{
    internal SigningDigest(Hash32 value) => Value = value;

    /// <summary>The 32 bytes a signer signs or a verifier checks.</summary>
    public Hash32 Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value.ToHex();
}

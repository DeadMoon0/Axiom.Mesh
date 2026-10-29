namespace Axiom.Mesh;

/// <summary>
/// An object whose tag this implementation does not know (SPEC.md §1.4): verified by its hash only, stored, replicated
/// and pinned by the rules of whatever references it, and never interpreted.
/// </summary>
public sealed class OpaqueObject : StorableObject
{
    private OpaqueObject(HashedObject source, byte tag)
        : base(source.Hash, source.Bytes) => RawTag = tag;

    /// <summary>The tag byte as received.</summary>
    public byte RawTag { get; }

    /// <inheritdoc />
    public override ObjectTag Tag => (ObjectTag)RawTag;

    internal static OpaqueObject Parse(HashedObject source, byte tag) => new(source, tag);
}

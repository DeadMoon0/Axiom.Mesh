namespace Axiom.Mesh;

/// <summary>What a Grant allows its grantee (SPEC.md §8.9); any other value makes the Grant malformed.</summary>
public enum GrantRights : byte
{
    /// <summary>Nothing: a revocation, which still reaches the revoked user (SPEC.md §9.7).</summary>
    None = 0,

    /// <summary>The grantee may read the Package: it is listed, claimed and served to them.</summary>
    Read = 1,

    /// <summary>The grantee may read and write: their devices' Entries are valid (SPEC.md §9.6).</summary>
    Write = 2,
}

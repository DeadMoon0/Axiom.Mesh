namespace Axiom.Mesh.Core;

/// <summary>Why this node wants an object (SPEC.md §10.2, §10.3): which step of replication asked for it.</summary>
internal enum WantKind
{
    /// <summary>No reason; never wanted.</summary>
    None = 0,

    /// <summary>A Package's Manifest a peer listed (§10.2 step 1, §10.6 for a listed Package).</summary>
    Manifest = 1,

    /// <summary>A Grant a peer listed (§10.2 step 2).</summary>
    Grant = 2,

    /// <summary>A Head a peer listed, from a device that may write (§10.2 step 3).</summary>
    Head = 3,

    /// <summary>A parent of a stored Entry (§10.3).</summary>
    Parent = 4,

    /// <summary>A payload object the retention policy asks for (§10.3, §11.2).</summary>
    Payload = 5,

    /// <summary>An object a local client fetches (LOCAL-SPEC.md, <c>Fetch</c>).</summary>
    Fetch = 6,
}

namespace Axiom.Mesh;

/// <summary>
/// What a Package's Entries mean (SPEC.md §9.2, §14.8). A kind this enumeration does not name is carried opaquely and
/// kept as its raw value: a node replicates, stores and orders the Entries of any kind.
/// </summary>
public enum PackageKind : ushort
{
    /// <summary>Kind 0, assigned to nothing; carried opaquely like any unknown kind.</summary>
    Unassigned = 0,

    /// <summary>Holds <c>Version</c> Entries (SPEC.md §9.5).</summary>
    Archive = 1,

    /// <summary>Holds <c>Message</c> and <c>Redaction</c> Entries (SPEC.md §9.4).</summary>
    Channel = 2,
}

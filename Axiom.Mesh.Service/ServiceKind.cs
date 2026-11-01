using System.Collections.Generic;

namespace Axiom.Mesh.Service;

/// <summary>
/// One Package kind a service defines and the Entry kinds in it (LOCAL-SPEC.md §9.1, §9.2): what it registers on every
/// connection, and what its Entry and Package subscriptions are filtered by.
/// </summary>
public sealed record ServiceKind
{
    /// <summary>The Package kind (SPEC.md §14.8).</summary>
    public required PackageKind Kind { get; init; }

    /// <summary>Its Entry kinds (SPEC.md §14.7), ascending and not empty, as <c>RegisterKind</c> takes them.</summary>
    public required IReadOnlyList<EntryKind> EntryKinds { get; init; }
}

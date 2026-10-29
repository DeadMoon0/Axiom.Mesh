using System.Collections.Generic;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// One service the Host's configuration lists (LOCAL-SPEC.md §4.2, §5.12): its name, how it proves the name, its token
/// for a spawned or external service, and the kinds it may register (§9.2).
/// </summary>
public sealed record LocalService
{
    /// <summary>The service's name: ASCII letters, digits, <c>.</c>, <c>-</c> and <c>_</c>, at most 32 bytes.</summary>
    public required string Name { get; init; }

    /// <summary>In-process, spawned or external.</summary>
    public required ServiceProvenance Provenance { get; init; }

    /// <summary>
    /// The token a spawned or external service presents in <c>Hello.token</c>, compared in time independent of its
    /// content; <see cref="ServiceToken.None"/> for an in-process service. For a spawned service, its current child's.
    /// </summary>
    public ServiceToken Token { get; init; }

    /// <summary>The Package kinds it may register, ascending.</summary>
    public IReadOnlyList<PackageKind> Kinds { get; init; } = [];

    /// <summary>The Entry kinds it may register, ascending.</summary>
    public IReadOnlyList<EntryKind> EntryKinds { get; init; } = [];
}

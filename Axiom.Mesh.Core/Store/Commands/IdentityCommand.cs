using System;

namespace Axiom.Mesh.Core;

/// <summary>Records the own identity (LOCAL-SPEC.md §6.1): the own user is known from then on, and Entries its device writes pin their references <c>Outbound</c>.</summary>
internal sealed class IdentityCommand : StoreCommand<bool>
{
    /// <summary>The identity to record.</summary>
    /// <param name="identity">The own user key and device.</param>
    public IdentityCommand(SelfIdentity identity) => Identity = identity ?? throw new ArgumentNullException(nameof(identity));

    /// <summary>The own user key and device.</summary>
    public SelfIdentity Identity { get; }
}

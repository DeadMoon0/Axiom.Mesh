using System;
using System.Collections.Generic;

namespace Axiom.Mesh.Core;

/// <summary>Keeps the static peers <c>AddStaticPeer</c> added (LOCAL-SPEC.md §6.6) in the index, replacing the list kept before.</summary>
internal sealed class StaticPeersCommand : StoreCommand<bool>
{
    /// <summary>The list to keep.</summary>
    /// <param name="addresses">The addresses, <c>host:port</c>.</param>
    public StaticPeersCommand(IReadOnlyList<string> addresses) => Addresses = addresses ?? throw new ArgumentNullException(nameof(addresses));

    /// <summary>The addresses.</summary>
    public IReadOnlyList<string> Addresses { get; }
}

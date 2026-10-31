using System.Collections.Generic;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// The substance checks of LOCAL-SPEC.md §9.4 <c>WriteEntry</c> that need nothing but the request: parents and references
/// are strictly ascending as byte strings, at most 2 and 64. A request that fails one is <c>Invalid</c>. The payload is
/// opaque and never parsed (decision 5): the body is the request's kind, payload, references and <c>redacts</c>, copied
/// into the envelope as given.
/// </summary>
internal static class WriteRule
{
    /// <summary>Checks a <c>WriteEntry</c>.</summary>
    /// <param name="request">The request.</param>
    /// <param name="body">What the Entry says, when the request passes.</param>
    /// <returns><see langword="null"/> when it passes; else why not.</returns>
    public static RequestRefusal? Check(WriteEntry request, out EntryBody? body)
    {
        body = null;
        if (!Ascending(request.Parents))
            return new RequestRefusal(Status.Invalid, "The parents must be ascending as byte strings and unique.", "parents");
        if (!EntryReferences.TryFromAscending(request.References, out EntryReferences references))
            return new RequestRefusal(Status.Invalid, $"The references must be ascending as byte strings, unique, and at most {Limits.MaxEntryReferences}.", "references");
        body = new EntryBody(request.Kind, request.Payload, references, request.Redacts);
        return null;
    }

    /// <summary>The parents of a request that passed <see cref="Check"/>.</summary>
    /// <param name="parents">At most two, ascending.</param>
    /// <returns>The parents; none for the Host's choice.</returns>
    public static EntryParents ParentsOf(IReadOnlyList<ObjectHash> parents) => parents.Count switch
    {
        0 => EntryParents.None,
        1 => EntryParents.Of(parents[0]),
        _ => EntryParents.Of(parents[0], parents[1]),
    };

    private static bool Ascending(IReadOnlyList<ObjectHash> hashes)
    {
        for (int i = 1; i < hashes.Count; i++)
        {
            if (hashes[i - 1].Value >= hashes[i].Value)
                return false;
        }

        return true;
    }
}

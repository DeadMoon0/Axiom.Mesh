using System.Collections.Generic;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// The substance checks of LOCAL-SPEC.md §9.4 <c>WriteEntry</c> that need nothing but the request (decision 5): a payload of
/// kinds 1 to 3 parses exactly to its end and comes with no stated references; parents and references are ascending as
/// byte strings and unique. A request that fails one is <c>Invalid</c>.
/// </summary>
internal static class WriteRule
{
    /// <summary>Checks a <c>WriteEntry</c>.</summary>
    /// <param name="request">The request.</param>
    /// <param name="payload">The payload parsed from a copy of the request's bytes, when it passes.</param>
    /// <returns><see langword="null"/> when it passes; else why not.</returns>
    public static RequestRefusal? Check(WriteEntry request, out EntryPayload? payload)
    {
        if (!EntryPayload.TryParse(request.Kind, request.Payload.ToArray(), out EntryPayload? parsed))
        {
            payload = null;
            return new RequestRefusal(Status.Invalid, $"The payload does not parse as an Entry of kind {(ushort)request.Kind} (SPEC.md §8.5).", "payload");
        }

        RequestRefusal? refusal = CheckLists(request, parsed is OpaquePayload);
        payload = refusal is null ? parsed : null;
        return refusal;
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

    private static RequestRefusal? CheckLists(WriteEntry request, bool opaque)
    {
        if (!opaque && request.References.Count != 0)
            return new RequestRefusal(Status.Invalid, "References are taken from the payload for kinds 1 to 3; state none.", "references");
        if (!Ascending(request.Parents))
            return new RequestRefusal(Status.Invalid, "The parents must be ascending as byte strings and unique.", "parents");
        if (!Ascending(request.References))
            return new RequestRefusal(Status.Invalid, "The references must be ascending as byte strings and unique.", "references");
        return null;
    }

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

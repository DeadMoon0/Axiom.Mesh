using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// <c>UpdatePackage</c> as a function of the current Manifest and the request (LOCAL-SPEC.md §6.4, SPEC.md §8.7): the owner
/// only; a name, when given, not empty; a retention, when given, 0 to 2; branches, when given, ascending by name as byte
/// strings, unique and at most 64. The next Manifest has <c>seq</c> one higher, the given fields and the current values of
/// the others.
/// </summary>
internal static class ManifestUpdateRule
{
    /// <summary>Why the update is refused.</summary>
    /// <param name="current">The current Manifest.</param>
    /// <param name="self">The own user.</param>
    /// <param name="request">The request.</param>
    /// <returns><see langword="null"/> when it may be made.</returns>
    public static RequestRefusal? Check(ManifestContent current, UserId self, UpdatePackage request)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(request);
        if (current.Owner != self)
            return new RequestRefusal(Status.NotOwner, "Only the Package's owner updates its Manifest; ask the owner.", string.Empty);
        if (request.Name is { Length: 0 })
            return new RequestRefusal(Status.Invalid, "The Package's name must not be empty.", "name");
        if (request.Retention is { } retention && (byte)retention > (byte)Retention.LatestOnly)
            return new RequestRefusal(Status.Invalid, $"Retention {(byte)retention} is not 0, 1 or 2.", "retention");
        if (request.Branches is { } branches && !AscendingAndUnique(branches))
            return new RequestRefusal(Status.Invalid, $"The branches must be ascending by name as byte strings, unique, and at most {Limits.MaxManifestBranches}.", "branches");
        return null;
    }

    /// <summary>The next Manifest's draft: seq + 1, the given fields over the current ones.</summary>
    /// <param name="current">The current Manifest.</param>
    /// <param name="request">The request, checked by <see cref="Check"/>.</param>
    /// <returns>The draft, to be signed with the user key.</returns>
    public static ManifestDraft Next(ManifestContent current, UpdatePackage request)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(request);
        return new ManifestDraft
        {
            Nonce = current.Nonce,
            Kind = current.Kind,
            Seq = current.Seq + 1,
            Name = request.Name ?? current.Name,
            Created = current.Created,
            Retention = request.Retention ?? current.Retention,
            Branches = request.Branches ?? current.Branches,
        };
    }

    private static bool AscendingAndUnique(IReadOnlyList<Branch> branches)
    {
        if (branches.Count > Limits.MaxManifestBranches)
            return false;
        byte[][] names = [.. branches.Select(b => Encoding.UTF8.GetBytes(b.Name))];
        for (int i = 1; i < names.Length; i++)
        {
            if (names[i - 1].AsSpan().SequenceCompareTo(names[i]) >= 0)
                return false;
        }

        return true;
    }
}

using System;
using System.Collections.Generic;

namespace Axiom.Mesh;

/// <summary>Who a Package is for: its owner and the rights of its current Grants (SPEC.md §8.9, §9.7).</summary>
public sealed class PackageAudience
{
    private readonly IReadOnlyDictionary<UserId, GrantRights> currentGrants;

    /// <summary>The audience of one Package.</summary>
    /// <param name="owner">The owner, from the current Manifest.</param>
    /// <param name="currentGrants">The rights of the current Grant per grantee; a user without one holds no rights.</param>
    public PackageAudience(UserId owner, IReadOnlyDictionary<UserId, GrantRights> currentGrants)
    {
        ArgumentNullException.ThrowIfNull(currentGrants);
        Owner = owner;
        this.currentGrants = currentGrants;
    }

    /// <summary>The owner.</summary>
    public UserId Owner { get; }

    /// <summary>The rights <paramref name="user"/>'s current Grant gives; <see cref="GrantRights.None"/> without one.</summary>
    /// <param name="user">A user.</param>
    /// <returns>The rights.</returns>
    public GrantRights RightsOf(UserId user) => currentGrants.TryGetValue(user, out GrantRights rights) ? rights : GrantRights.None;

    /// <summary>Whether <paramref name="user"/> is the owner or holds a current <c>Read</c> or <c>Write</c> Grant.</summary>
    /// <param name="user">A user.</param>
    /// <returns><see langword="true"/> when the user may see the Package.</returns>
    public bool Includes(UserId user) => user == Owner || RightsOf(user) != GrantRights.None;
}

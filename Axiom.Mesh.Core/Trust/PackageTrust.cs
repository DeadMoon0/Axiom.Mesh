using System.Collections.Generic;
using System.Linq;

namespace Axiom.Mesh.Core;

/// <summary>What the node holds of one Package: its state, current Manifest and Grants, local policy (SPEC.md §8.7, §8.9).</summary>
internal sealed class PackageTrust
{
    /// <summary>A Package as the node holds it.</summary>
    /// <param name="id">The Package id.</param>
    /// <param name="state">Listed or followed.</param>
    /// <param name="manifest">The current Manifest, or <see langword="null"/>.</param>
    /// <param name="grants">The current Grant per grantee.</param>
    /// <param name="policy">The local retention policy.</param>
    public PackageTrust(PackageId id, PackageState state, VerifiedManifest? manifest, IReadOnlyDictionary<UserId, VerifiedGrant> grants, LocalRetention policy)
    {
        Id = id;
        State = state;
        Manifest = manifest;
        Grants = grants;
        Policy = policy;
        Audience = manifest is null ? null : new PackageAudience(manifest.Content.Owner, grants.ToDictionary(g => g.Key, g => g.Value.Content.Rights));
    }

    /// <summary>The Package id.</summary>
    public PackageId Id { get; }

    /// <summary>Listed or followed.</summary>
    public PackageState State { get; }

    /// <summary>Whether the node follows it.</summary>
    public bool IsFollowed => State == PackageState.Followed;

    /// <summary>The current Manifest, or <see langword="null"/> while none is held.</summary>
    public VerifiedManifest? Manifest { get; }

    /// <summary>The current Grant per grantee.</summary>
    public IReadOnlyDictionary<UserId, VerifiedGrant> Grants { get; }

    /// <summary>The local retention policy (LOCAL-SPEC.md §14.14).</summary>
    public LocalRetention Policy { get; }

    /// <summary>The owner and the current Grants' rights, or <see langword="null"/> without a Manifest.</summary>
    public PackageAudience? Audience { get; }
}

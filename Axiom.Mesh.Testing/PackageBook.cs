using System;
using System.Collections.Generic;

namespace Axiom.Mesh.Testing;

/// <summary>
/// The Packages the nodes of one test mesh created, with their Manifests and the seq of the last Grant per grantee, so
/// that any node of the owner can sign the next Grant. Nonces count up from the mesh's seed: ids are reproducible.
/// Thread-safe.
/// </summary>
internal sealed class PackageBook
{
    private readonly object gate = new();
    private readonly Dictionary<PackageId, VerifiedManifest> manifests = [];
    private readonly Dictionary<(PackageId, UserId), ulong> grantSeqs = [];
    private readonly ulong seed;
    private ulong created;

    /// <summary>A book for a mesh.</summary>
    /// <param name="seed">The mesh's seed.</param>
    public PackageBook(ulong seed) => this.seed = seed;

    /// <summary>The next nonce: <c>H(seed ‖ n)</c> for the n-th Package of the mesh.</summary>
    /// <returns>The nonce.</returns>
    public PackageNonce NextNonce()
    {
        ulong n;
        lock (gate)
            n = ++created;
        byte[] input = new byte[16];
        BitConverter.TryWriteBytes(input.AsSpan(0, 8), seed);
        BitConverter.TryWriteBytes(input.AsSpan(8, 8), n);
        return new PackageNonce(Hash32.Of(input));
    }

    /// <summary>Records a Package's current Manifest.</summary>
    /// <param name="manifest">The Manifest.</param>
    public void Add(VerifiedManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        lock (gate)
            manifests[manifest.Content.Package] = manifest;
    }

    /// <summary>The Manifest a node of the mesh made for a Package.</summary>
    /// <param name="package">The Package.</param>
    /// <returns>The Manifest.</returns>
    /// <exception cref="ArgumentException">No node of the mesh created the Package.</exception>
    public VerifiedManifest ManifestOf(PackageId package)
    {
        lock (gate)
        {
            return manifests.TryGetValue(package, out VerifiedManifest? manifest) ? manifest
                : throw new ArgumentException($"No node of this mesh created Package {package}; create it with TestNode.CreatePackageAsync.", nameof(package));
        }
    }

    /// <summary>The seq the next Grant of a Package to a user carries: one more than the last.</summary>
    /// <param name="package">The Package.</param>
    /// <param name="grantee">The grantee.</param>
    /// <returns>The seq.</returns>
    public ulong NextGrantSeq(PackageId package, UserId grantee)
    {
        lock (gate)
        {
            ulong next = grantSeqs.GetValueOrDefault((package, grantee)) + 1;
            grantSeqs[(package, grantee)] = next;
            return next;
        }
    }
}

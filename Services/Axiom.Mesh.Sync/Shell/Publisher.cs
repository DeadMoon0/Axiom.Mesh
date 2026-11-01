using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;
using Axiom.Mesh.Payloads;

namespace Axiom.Mesh.Sync;

/// <summary>
/// Writes Versions (SYNC-SERVICE.md §3 step 8, LOCAL-SPEC.md §9.4, §9.5): the Trees put first, then the Version with its parents
/// named explicitly and ascending, <c>references</c> = its Tree. Never a Version whose Tree equals the Tree of a leaf this device
/// already wrote: that leaf stands for it, so a crash between writing and recording never makes a second one.
/// </summary>
internal static class Publisher
{
    /// <summary>Puts Trees, each pinned for the service until the run releases it.</summary>
    /// <param name="client">The service's connection.</param>
    /// <param name="trees">The Trees.</param>
    /// <param name="cancellationToken">Cancels the puts.</param>
    /// <returns>The hashes put.</returns>
    /// <exception cref="IOException">The Host refused a put.</exception>
    public static async Task<IReadOnlyList<ObjectHash>> PutTreesAsync(LocalClient client, IEnumerable<Tree> trees, CancellationToken cancellationToken)
    {
        var put = new List<ObjectHash>();
        foreach (Tree tree in trees.DistinctBy(t => t.Hash))
        {
            LocalResult<StoredObject> stored = await client.SendAsync(new PutObject { Object = tree.Bytes }, cancellationToken).ConfigureAwait(false);
            if (!stored.IsOk)
                throw new IOException($"The Host refused the Tree {tree.Hash}: {stored.Status} {stored.Error?.Message}");
            put.Add(tree.Hash);
        }

        return put;
    }

    /// <summary>Writes a Version, or finds the leaf of this device that already names the Tree.</summary>
    /// <param name="client">The service's connection.</param>
    /// <param name="versions">The Archive's Versions now.</param>
    /// <param name="package">The Archive.</param>
    /// <param name="label">The branch.</param>
    /// <param name="tree">The Tree, put already.</param>
    /// <param name="parents">The parents, explicit.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The Entry written; <see langword="null"/> when a leaf of this device already names the Tree (<see cref="OwnLeaf"/> finds it).</returns>
    /// <exception cref="IOException">The Host refused the write.</exception>
    public static async Task<Entry?> WriteAsync(LocalClient client, IReadOnlyDictionary<ObjectHash, VersionFact> versions, PackageId package, string label, ObjectHash tree, IReadOnlyList<ObjectHash> parents, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        DeviceId own = client.Host.Identity.DeviceId;
        if (Leaves.Of(versions, label).Any(l => l.Key.Device == own && l.Tree == tree))
            return null;
        var version = new VersionPayload { Tree = tree, Label = label, Prerelease = string.Empty, Notes = string.Empty };
        WriteEntry write = WriteEntry.Of(package, version.ToBody()) with { Parents = [.. parents.Distinct().Order()] };
        LocalResult<Entry> written = await client.SendAsync(write, cancellationToken).ConfigureAwait(false);
        if (!written.IsOk)
            throw new IOException($"The Host refused the Version: {written.Status} {written.Error?.Message}");
        return written.Value;
    }

    /// <summary>The leaf of this device that names a Tree.</summary>
    /// <param name="versions">The Archive's Versions.</param>
    /// <param name="label">The branch.</param>
    /// <param name="own">This device.</param>
    /// <param name="tree">The Tree.</param>
    /// <returns>The leaf, or <see langword="null"/>.</returns>
    public static VersionFact? OwnLeaf(IReadOnlyDictionary<ObjectHash, VersionFact> versions, string label, DeviceId own, ObjectHash tree) =>
        Leaves.Of(versions, label).LastOrDefault(l => l.Key.Device == own && l.Tree == tree);
}

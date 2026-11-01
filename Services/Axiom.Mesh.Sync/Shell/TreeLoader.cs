using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Sync;

/// <summary>
/// Reads a Tree and every Tree below it into a map (SPEC.md §8.4), through <see cref="ObjectReader"/>: straight from the store
/// directory where the Host allows it. A Tree not stored here is fetched from the peers once.
/// </summary>
internal static class TreeLoader
{
    /// <summary>Loads a Tree and every Tree below it.</summary>
    /// <param name="reader">Where objects are read.</param>
    /// <param name="client">The service's connection, for a fetch.</param>
    /// <param name="package">The Archive the Tree belongs to.</param>
    /// <param name="root">The Tree.</param>
    /// <param name="trees">The map the Trees are added to; Trees already in it are not read again.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns><see langword="true"/> when every Tree is at hand; <see langword="false"/> when one is not stored and no peer supplied it.</returns>
    public static async Task<bool> LoadAsync(ObjectReader reader, LocalClient client, PackageId package, ObjectHash root, Dictionary<ObjectHash, Tree> trees, CancellationToken cancellationToken)
    {
        if (await TryLoadAsync(reader, root, trees, cancellationToken).ConfigureAwait(false))
            return true;
        return await ObjectFetcher.FetchAsync(client, package, root, cancellationToken).ConfigureAwait(false)
            && await TryLoadAsync(reader, root, trees, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> TryLoadAsync(ObjectReader reader, ObjectHash root, Dictionary<ObjectHash, Tree> trees, CancellationToken cancellationToken)
    {
        var pending = new Stack<ObjectHash>([root]);
        while (pending.TryPop(out ObjectHash hash))
        {
            if (!trees.TryGetValue(hash, out Tree? tree))
            {
                LocalResult<ParsedObject> read = await reader.ReadAsync(hash, cancellationToken).ConfigureAwait(false);
                if (!read.IsOk || read.Value is not Tree parsed)
                    return false;
                trees[hash] = tree = parsed;
            }

            foreach (TreeEntry entry in tree.Entries)
            {
                if (entry.Mode == TreeEntryMode.Directory && !trees.ContainsKey(entry.Hash))
                    pending.Push(entry.Hash);
            }
        }

        return true;
    }
}

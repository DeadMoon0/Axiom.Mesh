using System.Collections.Generic;

namespace Axiom.Mesh.Core;

/// <summary>
/// What a stored object names, read off it while its bytes are at hand (SPEC.md §10.3): an Entry's parents and payload
/// objects, a Tree's entries, a ChunkList's Chunks. The history walk wants from these, so it never reads an object back.
/// </summary>
internal sealed class ObjectLinks
{
    private ObjectLinks(IReadOnlyList<ObjectHash> parents, IReadOnlyList<ObjectLink> children, bool newestVersion)
    {
        Parents = parents;
        Children = children;
        IsNewestVersion = newestVersion;
    }

    /// <summary>An object that names nothing: a Chunk, a Roster, a Manifest, a Grant, an opaque object.</summary>
    public static ObjectLinks None { get; } = new([], [], false);

    /// <summary>An Entry's parents; empty for every other object.</summary>
    public IReadOnlyList<ObjectHash> Parents { get; }

    /// <summary>The payload objects of an Entry, the entries of a Tree, the Chunks of a ChunkList.</summary>
    public IReadOnlyList<ObjectLink> Children { get; }

    /// <summary>Whether this is a Version that became its Package's newest when stored (SPEC.md §11.2, phone row).</summary>
    public bool IsNewestVersion { get; }

    /// <summary>What a storable object names.</summary>
    /// <param name="storable">The object.</param>
    /// <param name="newestVersion">For a Version, whether it is now its Package's newest.</param>
    /// <returns>Its links.</returns>
    public static ObjectLinks Of(StorableObject storable, bool newestVersion) => storable switch
    {
        AdmittedEntry entry => OfEntry(entry.Content, newestVersion),
        Tree tree => new([], OfTree(tree), false),
        ChunkList list => new([], OfChunkList(list), false),
        _ => None,
    };

    private static ObjectLinks OfEntry(EntryContent content, bool newestVersion)
    {
        var parents = new ObjectHash[content.Parents.Count];
        for (int i = 0; i < parents.Length; i++)
            parents[i] = content.Parents[i];

        ObjectTag tag = content.Payload is VersionPayload ? ObjectTag.Tree : ObjectTag.Unassigned;
        IReadOnlyList<ObjectHash> references = content.Payload.References;
        var children = new ObjectLink[references.Count];
        for (int i = 0; i < children.Length; i++)
            children[i] = new ObjectLink(references[i], tag);
        return new ObjectLinks(parents, children, newestVersion && content.Kind == EntryKind.Version);
    }

    private static ObjectLink[] OfTree(Tree tree)
    {
        var children = new ObjectLink[tree.Entries.Count];
        for (int i = 0; i < children.Length; i++)
        {
            TreeEntry entry = tree.Entries[i];
            ObjectTag tag = entry.Mode switch
            {
                TreeEntryMode.File or TreeEntryMode.ExecutableFile => ObjectTag.ChunkList,
                TreeEntryMode.Directory => ObjectTag.Tree,
                TreeEntryMode.Symlink => ObjectTag.Chunk,
                _ => ObjectTag.Unassigned,
            };
            children[i] = new ObjectLink(entry.Hash, tag);
        }

        return children;
    }

    private static ObjectLink[] OfChunkList(ChunkList list)
    {
        var children = new ObjectLink[list.Chunks.Count];
        for (int i = 0; i < children.Length; i++)
            children[i] = new ObjectLink(list.Chunks[i], ObjectTag.Chunk);
        return children;
    }
}

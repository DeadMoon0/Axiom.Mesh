using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Payloads;

namespace Axiom.Mesh.Testing;

/// <summary>
/// Content for replication tests: deterministic bytes, a folder of files cut into Chunks with its Tree, and a Package
/// filled with Messages. Everything is made with Model's producers, so it is exactly what a real writer would store.
/// </summary>
public static class ObjectFixtures
{
    /// <summary><paramref name="length"/> bytes that depend on <paramref name="seed"/> and the position only.</summary>
    /// <param name="length">How many.</param>
    /// <param name="seed">What makes them differ from other content.</param>
    /// <returns>The bytes.</returns>
    public static byte[] Content(int length, byte seed)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        byte[] bytes = new byte[length];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)((i * 31) ^ (i >> 8) ^ seed);
        return bytes;
    }

    /// <summary>
    /// A folder of files, each cut into Chunks of 1 MiB and the remainder (SPEC.md §8.2), named by a ChunkList, all
    /// named by one Tree sorted by name (§8.4).
    /// </summary>
    /// <param name="files">The files: a name and the content.</param>
    /// <returns>The folder.</returns>
    /// <exception cref="ArgumentException">Names that a Tree refuses: empty, repeated, or with a separator.</exception>
    public static TreeFixture Folder(params (string Name, byte[] Content)[] files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var objects = new List<StorableObject>();
        var chunks = new List<Chunk>();
        var entries = new List<TreeEntry>();
        foreach ((string name, byte[] content) in files.OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            ChunkedFile cut = Chunker.Cut(content);
            chunks.AddRange(cut.Chunks);
            objects.AddRange(cut.Chunks.DistinctBy(c => c.Hash));
            objects.Add(cut.List);
            entries.Add(new TreeEntry(name, TreeEntryMode.File, cut.List.Hash, cut.List.Size));
        }

        if (!ObjectBuilders.TryTree(entries, out Tree? root, out string? problem))
            throw new ArgumentException("The files do not make a Tree: " + problem, nameof(files));
        objects.Add(root);
        return new TreeFixture(root, objects, chunks);
    }

    /// <summary>Writes <paramref name="count"/> Messages to a Package, one after the other, from one node.</summary>
    /// <param name="node">The writing node; its user owns the Package or holds a Write grant.</param>
    /// <param name="package">The Package.</param>
    /// <param name="count">How many.</param>
    /// <param name="cancellationToken">Cancels writing.</param>
    /// <returns>The Entries' hashes, in the order written.</returns>
    public static async Task<IReadOnlyList<ObjectHash>> WriteMessagesAsync(TestNode node, PackageId package, int count, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(node);
        var written = new List<ObjectHash>(count);
        for (int i = 0; i < count; i++)
            written.Add(await node.WriteEntryAsync(package, Message($"message {i + 1} from {node.Name}"), cancellationToken).ConfigureAwait(false));
        return written;
    }

    /// <summary>A Message payload with text and no attachments.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The payload.</returns>
    public static MessagePayload Message(string text) => new() { Text = text, Attachments = [], ReplyTo = default };

    /// <summary>A Version payload naming a folder's root Tree on branch <c>main</c>.</summary>
    /// <param name="folder">The folder.</param>
    /// <returns>The payload.</returns>
    public static VersionPayload Version(TreeFixture folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        return new VersionPayload { Tree = folder.Root.Hash, Label = "main", Prerelease = string.Empty, Notes = string.Empty };
    }
}

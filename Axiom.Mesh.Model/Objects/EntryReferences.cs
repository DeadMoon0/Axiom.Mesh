using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh;

/// <summary>
/// The objects an Entry needs (SPEC.md §8.6 <c>references</c>): Trees, ChunkLists, attachments, whatever its payload
/// names. At most 64, strictly ascending as byte strings, each once; a value of this type is canonical by construction,
/// so a producer cannot sign a malformed list. The node walks them (and through Trees and ChunkLists everything they
/// name) for visibility, pins and wants, and never reads the payload to learn them.
/// </summary>
/// <remarks><c>default</c> is the empty list. A parsed list refers to the object's bytes without copying.</remarks>
public readonly struct EntryReferences : IReadOnlyList<ObjectHash>
{
    private readonly ReadOnlyMemory<byte> bytes;

    private EntryReferences(ReadOnlyMemory<byte> bytes) => this.bytes = bytes;

    /// <summary>No references.</summary>
    public static EntryReferences None => default;

    /// <summary>The number of references, 0 to 64.</summary>
    public int Count => bytes.Length / Hash32.Length;

    /// <summary>Whether there are none.</summary>
    public bool IsEmpty => bytes.IsEmpty;

    /// <summary>The reference at <paramref name="index"/>, in ascending order.</summary>
    /// <param name="index">From 0 to <see cref="Count"/> − 1.</param>
    /// <returns>The hash.</returns>
    public ObjectHash this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count)
                throw new ArgumentOutOfRangeException(nameof(index), index, $"The Entry references {Count} objects.");
            return new ObjectHash(new Hash32(bytes.Span.Slice(index * Hash32.Length, Hash32.Length)));
        }
    }

    /// <summary>The references of a set of hashes in any order: sorted, each kept once.</summary>
    /// <param name="hashes">The objects the Entry needs.</param>
    /// <returns>The references.</returns>
    /// <exception cref="ArgumentException">More than 64 distinct hashes.</exception>
    public static EntryReferences Of(IEnumerable<ObjectHash> hashes)
    {
        ArgumentNullException.ThrowIfNull(hashes);
        ObjectHash[] sorted = [.. hashes.Distinct().Order()];
        if (sorted.Length > Limits.MaxEntryReferences)
            throw new ArgumentException($"An Entry references at most {Limits.MaxEntryReferences} objects; {sorted.Length} were given (SPEC.md §8.6).", nameof(hashes));
        return new EntryReferences(Encode(sorted));
    }

    /// <summary>
    /// The references of a list that must already be canonical, as a request on the wire states it (LOCAL-SPEC.md §9.4):
    /// at most 64, strictly ascending as byte strings.
    /// </summary>
    /// <param name="hashes">The list as stated.</param>
    /// <param name="references">The references, when the list is canonical.</param>
    /// <returns><see langword="false"/> when the list is longer than 64, repeats a hash or is out of order.</returns>
    public static bool TryFromAscending(IReadOnlyList<ObjectHash> hashes, out EntryReferences references)
    {
        ArgumentNullException.ThrowIfNull(hashes);
        references = default;
        if (hashes.Count > Limits.MaxEntryReferences)
            return false;
        for (int i = 1; i < hashes.Count; i++)
        {
            if (hashes[i - 1] >= hashes[i])
                return false;
        }

        references = new EntryReferences(Encode(hashes));
        return true;
    }

    /// <summary>Whether <paramref name="hash"/> is referenced.</summary>
    /// <param name="hash">The hash.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public bool Contains(ObjectHash hash)
    {
        int low = 0;
        int high = Count - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            int order = this[middle].CompareTo(hash);
            if (order == 0)
                return true;
            if (order < 0)
                low = middle + 1;
            else
                high = middle - 1;
        }

        return false;
    }

    /// <summary>Enumerates the references in ascending order, without allocating.</summary>
    /// <returns>The enumerator.</returns>
    public Enumerator GetEnumerator() => new(this);

    IEnumerator<ObjectHash> IEnumerable<ObjectHash>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Reads a <c>list&lt;hash&gt;(64)</c>; a list that is not strictly ascending fails the reader.</summary>
    internal static EntryReferences Read(ref WireReader reader)
    {
        int count = Wire.ReadListCount(ref reader, Limits.MaxEntryReferences);
        ReadOnlyMemory<byte> view = reader.BytesView(count * Hash32.Length);
        if (!reader.Ok)
            return default;

        var read = new EntryReferences(view);
        for (int i = 1; i < count; i++)
        {
            if (read[i - 1] >= read[i])
            {
                reader.Fail();
                return default;
            }
        }

        return read;
    }

    internal void Write(ref WireWriter writer)
    {
        Wire.WriteListCount(ref writer, Count, Limits.MaxEntryReferences);
        writer.Bytes(bytes.Span);
    }

    private static byte[] Encode(IReadOnlyList<ObjectHash> hashes)
    {
        byte[] encoded = new byte[hashes.Count * Hash32.Length];
        for (int i = 0; i < hashes.Count; i++)
            hashes[i].Value.CopyTo(encoded.AsSpan(i * Hash32.Length, Hash32.Length));
        return encoded;
    }

    /// <summary>Enumerates <see cref="EntryReferences"/>.</summary>
    public struct Enumerator : IEnumerator<ObjectHash>
    {
        private readonly EntryReferences references;
        private int index;

        internal Enumerator(EntryReferences references)
        {
            this.references = references;
            index = -1;
        }

        /// <inheritdoc />
        public readonly ObjectHash Current => references[index];

        readonly object IEnumerator.Current => Current;

        /// <inheritdoc />
        public bool MoveNext() => ++index < references.Count;

        /// <inheritdoc />
        public void Reset() => index = -1;

        /// <inheritdoc />
        public readonly void Dispose()
        {
        }
    }
}

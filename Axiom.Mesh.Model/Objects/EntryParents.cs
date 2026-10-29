using System;
using System.Collections;
using System.Collections.Generic;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh;

/// <summary>
/// The Entries an Entry follows: none, one, or two, ascending as byte strings and distinct (SPEC.md §8.6). Held inline;
/// the order is established when the value is made, so a value of this type is always canonical.
/// </summary>
[ClosedShape("An Entry has at most two parents (SPEC.md §8.6).")]
public readonly record struct EntryParents : IReadOnlyList<ObjectHash>
{
    private readonly ObjectHash first;
    private readonly ObjectHash second;
    private readonly int count;

    private EntryParents(int count, ObjectHash first, ObjectHash second)
    {
        this.count = count;
        this.first = first;
        this.second = second;
    }

    /// <summary>No parents: the first Entry a device writes to a Package it holds nothing of (SPEC.md §9.3).</summary>
    public static EntryParents None => default;

    /// <summary>The number of parents, 0 to 2.</summary>
    public int Count => count;

    /// <summary>Whether there are no parents.</summary>
    public bool IsEmpty => count == 0;

    /// <summary>The parent at <paramref name="index"/>, in ascending order.</summary>
    /// <param name="index">0 or 1, below <see cref="Count"/>.</param>
    /// <returns>The parent.</returns>
    public ObjectHash this[int index] => (uint)index < (uint)count
        ? (index == 0 ? first : second)
        : throw new ArgumentOutOfRangeException(nameof(index), index, $"The Entry has {count} parents.");

    /// <summary>One parent.</summary>
    /// <param name="parent">The parent.</param>
    /// <returns>The parents.</returns>
    public static EntryParents Of(ObjectHash parent) => new(1, parent, default);

    /// <summary>Two parents, in either order; they are sorted.</summary>
    /// <param name="one">One parent.</param>
    /// <param name="other">The other parent.</param>
    /// <returns>The parents, ascending.</returns>
    /// <exception cref="ArgumentException">The two are the same Entry.</exception>
    public static EntryParents Of(ObjectHash one, ObjectHash other)
    {
        if (one == other)
            throw new ArgumentException("An Entry cannot name the same parent twice (SPEC.md §8.6).", nameof(other));
        return one < other ? new EntryParents(2, one, other) : new EntryParents(2, other, one);
    }

    /// <summary>Whether <paramref name="hash"/> is one of the parents.</summary>
    /// <param name="hash">The hash.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public bool Contains(ObjectHash hash) => (count > 0 && first == hash) || (count > 1 && second == hash);

    /// <summary>Enumerates the parents in ascending order, without allocating.</summary>
    /// <returns>The enumerator.</returns>
    public Enumerator GetEnumerator() => new(this);

    IEnumerator<ObjectHash> IEnumerable<ObjectHash>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Reads a <c>list&lt;hash&gt;(2)</c>; a list that is not strictly ascending fails the reader.</summary>
    internal static EntryParents Read(ref WireReader reader)
    {
        int length = Wire.ReadListCount(ref reader, Limits.MaxEntryParents);
        ObjectHash a = length > 0 ? ObjectHash.Read(ref reader) : default;
        ObjectHash b = length > 1 ? ObjectHash.Read(ref reader) : default;
        if (length == 2 && a >= b)
            reader.Fail();
        return reader.Ok ? new EntryParents(length, a, b) : default;
    }

    internal void Write(ref WireWriter writer)
    {
        Wire.WriteListCount(ref writer, count, Limits.MaxEntryParents);
        foreach (ObjectHash parent in this)
            parent.Write(ref writer);
    }

    /// <summary>Enumerates <see cref="EntryParents"/>.</summary>
    public struct Enumerator : IEnumerator<ObjectHash>
    {
        private readonly EntryParents parents;
        private int index;

        internal Enumerator(EntryParents parents)
        {
            this.parents = parents;
            index = -1;
        }

        /// <inheritdoc />
        public readonly ObjectHash Current => parents[index];

        readonly object IEnumerator.Current => Current;

        /// <inheritdoc />
        public bool MoveNext() => ++index < parents.Count;

        /// <inheritdoc />
        public void Reset() => index = -1;

        /// <inheritdoc />
        public readonly void Dispose()
        {
        }
    }
}

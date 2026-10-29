using System;
using System.Collections;
using System.Collections.Generic;

namespace Axiom.Mesh;

/// <summary>
/// A list of hashes read in place from an object's bytes: 32 bytes per element, decoded when indexed, never copied.
/// </summary>
public readonly struct HashList : IReadOnlyList<ObjectHash>
{
    private readonly ReadOnlyMemory<byte> bytes;

    internal HashList(ReadOnlyMemory<byte> bytes) => this.bytes = bytes;

    /// <summary>The number of hashes.</summary>
    public int Count => bytes.Length / Hash32.Length;

    /// <summary>The hash at <paramref name="index"/>.</summary>
    /// <param name="index">From 0 to <see cref="Count"/> − 1.</param>
    /// <returns>The hash.</returns>
    public ObjectHash this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count)
                throw new ArgumentOutOfRangeException(nameof(index), index, $"The list holds {Count} hashes.");
            return new ObjectHash(new Hash32(bytes.Span.Slice(index * Hash32.Length, Hash32.Length)));
        }
    }

    /// <summary>Enumerates the hashes in order, without allocating.</summary>
    /// <returns>The enumerator.</returns>
    public Enumerator GetEnumerator() => new(this);

    IEnumerator<ObjectHash> IEnumerable<ObjectHash>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Enumerates a <see cref="HashList"/>.</summary>
    public struct Enumerator : IEnumerator<ObjectHash>
    {
        private readonly HashList list;
        private int index;

        internal Enumerator(HashList list)
        {
            this.list = list;
            index = -1;
        }

        /// <inheritdoc />
        public readonly ObjectHash Current => list[index];

        readonly object IEnumerator.Current => Current;

        /// <inheritdoc />
        public bool MoveNext() => ++index < list.Count;

        /// <inheritdoc />
        public void Reset() => index = -1;

        /// <inheritdoc />
        public readonly void Dispose()
        {
        }
    }
}

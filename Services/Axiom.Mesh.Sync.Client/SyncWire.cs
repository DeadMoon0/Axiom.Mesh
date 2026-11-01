using System;
using System.Collections.Generic;
using Axiom.Mesh.Local;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Sync;

/// <summary>
/// The structures the records of <c>axiom-mesh-sync/1</c> share (SYNC-SERVICE.md §3), over the types of SPEC.md §5.2: lists
/// of strings and of records. Readers never throw and fail the reader on a value out of bounds; writers refuse one with an
/// <see cref="ArgumentException"/>.
/// </summary>
internal static class SyncWire
{
    /// <summary>Reads a <c>list&lt;string(maxBytes)&gt;(maxCount)</c>.</summary>
    public static IReadOnlyList<string> ReadStrings(ref WireReader reader, int maxCount, int maxBytes)
    {
        int count = Wire.ReadListCount(ref reader, maxCount);
        if (count == 0)
            return [];
        string[] items = new string[count];
        for (int i = 0; i < count && reader.Ok; i++)
            items[i] = Wire.ReadString(ref reader, maxBytes);
        return reader.Ok ? items : [];
    }

    /// <summary>Writes a <c>list&lt;string(maxBytes)&gt;(maxCount)</c>.</summary>
    public static void WriteStrings(ref WireWriter writer, IReadOnlyList<string> items, int maxCount, int maxBytes)
    {
        ArgumentNullException.ThrowIfNull(items);
        Wire.WriteListCount(ref writer, items.Count, maxCount);
        for (int i = 0; i < items.Count; i++)
            Wire.WriteString(ref writer, items[i] ?? string.Empty, maxBytes);
    }

    /// <summary>Reads one record; only meaningful while <c>reader.Ok</c>.</summary>
    public static T ReadRecord<T>(ref WireReader reader)
        where T : class, IWireRecord<T> =>
        T.TryRead(ref reader, out T? value) ? value : null!;

    /// <summary>Reads a <c>list&lt;T&gt;(max)</c> of records.</summary>
    public static IReadOnlyList<T> ReadList<T>(ref WireReader reader, int maxCount)
        where T : class, IWireRecord<T>
    {
        int count = Wire.ReadListCount(ref reader, maxCount);
        if (count == 0)
            return [];
        var items = new T[count];
        for (int i = 0; i < count; i++)
        {
            if (!T.TryRead(ref reader, out T? item))
                return [];
            items[i] = item;
        }

        return items;
    }

    /// <summary>Writes a <c>list&lt;T&gt;(max)</c> of records.</summary>
    public static void WriteList<T>(ref WireWriter writer, IReadOnlyList<T> items, int maxCount)
        where T : class, IWireRecord<T>
    {
        ArgumentNullException.ThrowIfNull(items);
        Wire.WriteListCount(ref writer, items.Count, maxCount);
        for (int i = 0; i < items.Count; i++)
            items[i].Write(ref writer);
    }
}

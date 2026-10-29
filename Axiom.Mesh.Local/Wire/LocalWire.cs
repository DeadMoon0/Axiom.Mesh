using System;
using System.Collections.Generic;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>
/// The types LOCAL-SPEC.md §3.2 and §5 add to SPEC.md §5.2, over <see cref="Wire"/>: copied blobs, lists of records,
/// ids, hashes, kinds and strings, and the AMP/1 structures a record carries (<see cref="Head"/>, <see cref="Branch"/>,
/// <see cref="EntryKey"/>). Every reader checks a count or a length against its maximum before it reads content and
/// never throws; every writer refuses a value outside its bounds.
/// </summary>
internal static class LocalWire
{
    /// <summary>Reads a <c>blob(max)</c> and copies it, so the value outlives the frame it came in.</summary>
    public static ReadOnlyMemory<byte> ReadBlob(ref WireReader reader, int maxBytes)
    {
        ReadOnlyMemory<byte> view = Wire.ReadBlob(ref reader, maxBytes);
        return reader.Ok && !view.IsEmpty ? view.ToArray() : ReadOnlyMemory<byte>.Empty;
    }

    /// <summary>Writes a <c>blob(max)</c>.</summary>
    public static void WriteBlob(ref WireWriter writer, ReadOnlyMemory<byte> value, int maxBytes) =>
        Wire.WriteBlob(ref writer, value.Span, maxBytes);

    /// <summary>Reads one record; the result is only meaningful while <c>reader.Ok</c>.</summary>
    public static T ReadRecord<T>(ref WireReader reader)
        where T : class, IWireRecord<T>
    {
        // Only reached with null when the reader failed, and then the caller discards what it built.
        return T.TryRead(ref reader, out T? value) ? value : null!;
    }

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

    /// <summary>Reads a <c>list&lt;hash&gt;(max)</c>.</summary>
    public static IReadOnlyList<ObjectHash> ReadHashes(ref WireReader reader, int maxCount)
    {
        int count = Wire.ReadListCount(ref reader, maxCount);
        if (count == 0)
            return [];

        var hashes = new ObjectHash[count];
        for (int i = 0; i < count; i++)
            hashes[i] = ObjectHash.Read(ref reader);
        return reader.Ok ? hashes : [];
    }

    /// <summary>Writes a <c>list&lt;hash&gt;(max)</c>.</summary>
    public static void WriteHashes(ref WireWriter writer, IReadOnlyList<ObjectHash> hashes, int maxCount)
    {
        ArgumentNullException.ThrowIfNull(hashes);
        Wire.WriteListCount(ref writer, hashes.Count, maxCount);
        for (int i = 0; i < hashes.Count; i++)
            hashes[i].Write(ref writer);
    }

    /// <summary>Reads a <c>list&lt;u16&gt;(max)</c>.</summary>
    public static IReadOnlyList<ushort> ReadUInt16s(ref WireReader reader, int maxCount)
    {
        int count = Wire.ReadListCount(ref reader, maxCount);
        if (count == 0)
            return [];

        var values = new ushort[count];
        for (int i = 0; i < count; i++)
            values[i] = reader.UInt16BigEndian();
        return reader.Ok ? values : [];
    }

    /// <summary>Writes a <c>list&lt;u16&gt;(max)</c>.</summary>
    public static void WriteUInt16s(ref WireWriter writer, IReadOnlyList<ushort> values, int maxCount)
    {
        ArgumentNullException.ThrowIfNull(values);
        Wire.WriteListCount(ref writer, values.Count, maxCount);
        for (int i = 0; i < values.Count; i++)
            writer.UInt16BigEndian(values[i]);
    }

    /// <summary>Reads a <c>list&lt;u16&gt;(max)</c> of Package kinds (SPEC.md §14.8); unknown kinds are carried.</summary>
    public static IReadOnlyList<PackageKind> ReadPackageKinds(ref WireReader reader, int maxCount)
    {
        int count = Wire.ReadListCount(ref reader, maxCount);
        if (count == 0)
            return [];

        var kinds = new PackageKind[count];
        for (int i = 0; i < count; i++)
            kinds[i] = (PackageKind)reader.UInt16BigEndian();
        return reader.Ok ? kinds : [];
    }

    /// <summary>Writes a <c>list&lt;u16&gt;(max)</c> of Package kinds.</summary>
    public static void WritePackageKinds(ref WireWriter writer, IReadOnlyList<PackageKind> kinds, int maxCount)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        Wire.WriteListCount(ref writer, kinds.Count, maxCount);
        for (int i = 0; i < kinds.Count; i++)
            writer.UInt16BigEndian((ushort)kinds[i]);
    }

    /// <summary>Reads a <c>list&lt;u16&gt;(max)</c> of Entry kinds (SPEC.md §14.7); unknown kinds are carried.</summary>
    public static IReadOnlyList<EntryKind> ReadEntryKinds(ref WireReader reader, int maxCount)
    {
        int count = Wire.ReadListCount(ref reader, maxCount);
        if (count == 0)
            return [];

        var kinds = new EntryKind[count];
        for (int i = 0; i < count; i++)
            kinds[i] = (EntryKind)reader.UInt16BigEndian();
        return reader.Ok ? kinds : [];
    }

    /// <summary>Writes a <c>list&lt;u16&gt;(max)</c> of Entry kinds.</summary>
    public static void WriteEntryKinds(ref WireWriter writer, IReadOnlyList<EntryKind> kinds, int maxCount)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        Wire.WriteListCount(ref writer, kinds.Count, maxCount);
        for (int i = 0; i < kinds.Count; i++)
            writer.UInt16BigEndian((ushort)kinds[i]);
    }

    /// <summary>Reads a <c>list&lt;string(itemMax)&gt;(max)</c>.</summary>
    public static IReadOnlyList<string> ReadStrings(ref WireReader reader, int maxBytes, int maxCount)
    {
        int count = Wire.ReadListCount(ref reader, maxCount);
        if (count == 0)
            return [];

        var values = new string[count];
        for (int i = 0; i < count; i++)
            values[i] = Wire.ReadString(ref reader, maxBytes);
        return reader.Ok ? values : [];
    }

    /// <summary>Writes a <c>list&lt;string(itemMax)&gt;(max)</c>.</summary>
    public static void WriteStrings(ref WireWriter writer, IReadOnlyList<string> values, int maxBytes, int maxCount)
    {
        ArgumentNullException.ThrowIfNull(values);
        Wire.WriteListCount(ref writer, values.Count, maxCount);
        for (int i = 0; i < values.Count; i++)
            Wire.WriteString(ref writer, values[i], maxBytes);
    }

    /// <summary>Reads a <c>Head</c> (SPEC.md §7.2): device id, Entry hash, clock.</summary>
    public static Head ReadHead(ref WireReader reader)
    {
        DeviceId device = DeviceId.Read(ref reader);
        ObjectHash entry = ObjectHash.Read(ref reader);
        ulong clock = reader.UInt64BigEndian();
        return new Head(device, entry, clock);
    }

    /// <summary>Writes a <c>Head</c> (SPEC.md §7.2).</summary>
    public static void WriteHead(ref WireWriter writer, Head head)
    {
        head.Device.Write(ref writer);
        head.Entry.Write(ref writer);
        writer.UInt64BigEndian(head.Clock);
    }

    /// <summary>Reads a <c>list&lt;Head&gt;(max)</c>.</summary>
    public static IReadOnlyList<Head> ReadHeads(ref WireReader reader, int maxCount)
    {
        int count = Wire.ReadListCount(ref reader, maxCount);
        if (count == 0)
            return [];

        var heads = new Head[count];
        for (int i = 0; i < count; i++)
            heads[i] = ReadHead(ref reader);
        return reader.Ok ? heads : [];
    }

    /// <summary>Writes a <c>list&lt;Head&gt;(max)</c>.</summary>
    public static void WriteHeads(ref WireWriter writer, IReadOnlyList<Head> heads, int maxCount)
    {
        ArgumentNullException.ThrowIfNull(heads);
        Wire.WriteListCount(ref writer, heads.Count, maxCount);
        for (int i = 0; i < heads.Count; i++)
            WriteHead(ref writer, heads[i]);
    }

    /// <summary>Reads a <c>list&lt;Branch&gt;(max)</c> (SPEC.md §8.7): name <c>string(64)</c>, head hash.</summary>
    public static IReadOnlyList<Branch> ReadBranches(ref WireReader reader, int maxCount)
    {
        int count = Wire.ReadListCount(ref reader, maxCount);
        if (count == 0)
            return [];

        var branches = new Branch[count];
        for (int i = 0; i < count; i++)
        {
            string name = Wire.ReadString(ref reader, Limits.MaxBranchNameLength);
            ObjectHash head = ObjectHash.Read(ref reader);
            branches[i] = new Branch(name, head);
        }

        return reader.Ok ? branches : [];
    }

    /// <summary>Writes a <c>list&lt;Branch&gt;(max)</c> (SPEC.md §8.7).</summary>
    public static void WriteBranches(ref WireWriter writer, IReadOnlyList<Branch> branches, int maxCount)
    {
        ArgumentNullException.ThrowIfNull(branches);
        Wire.WriteListCount(ref writer, branches.Count, maxCount);
        for (int i = 0; i < branches.Count; i++)
        {
            Wire.WriteString(ref writer, branches[i].Name, Limits.MaxBranchNameLength);
            branches[i].Head.Write(ref writer);
        }
    }

    /// <summary>Reads an <c>EntryKey</c> (LOCAL-SPEC.md §5.6): clock, device id, hash.</summary>
    public static EntryKey ReadEntryKey(ref WireReader reader)
    {
        ulong clock = reader.UInt64BigEndian();
        DeviceId device = DeviceId.Read(ref reader);
        ObjectHash hash = ObjectHash.Read(ref reader);
        return new EntryKey(clock, device, hash);
    }

    /// <summary>Writes an <c>EntryKey</c> (LOCAL-SPEC.md §5.6).</summary>
    public static void WriteEntryKey(ref WireWriter writer, EntryKey key)
    {
        writer.UInt64BigEndian(key.Clock);
        key.Device.Write(ref writer);
        key.Hash.Write(ref writer);
    }
}

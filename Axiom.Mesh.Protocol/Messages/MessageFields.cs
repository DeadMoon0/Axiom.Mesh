using System;
using System.Collections.Generic;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Protocol;

/// <summary>
/// The list fields AMP/1 messages share, over <see cref="Wire"/>: each reader checks the count against its maximum and
/// against the bytes left before it allocates or reads an element (SPEC.md §5.2), and fails the reader instead of
/// throwing; each writer refuses a list over its maximum with an <see cref="ArgumentException"/>.
/// </summary>
internal static class MessageFields
{
    /// <summary>Reads a <c>list&lt;hash&gt;(max)</c>.</summary>
    public static ObjectHash[] ReadHashes(ref WireReader reader, int maxCount)
    {
        int count = Wire.ReadListCount(ref reader, maxCount);
        if (!reader.Ok || !Fits(ref reader, count, Hash32.Length))
            return [];

        var hashes = new ObjectHash[count];
        for (int i = 0; i < count; i++)
            hashes[i] = ObjectHash.Read(ref reader);
        return reader.Ok ? hashes : [];
    }

    /// <summary>Writes a <c>list&lt;hash&gt;(max)</c>.</summary>
    public static void WriteHashes(ref WireWriter writer, IReadOnlyList<ObjectHash> hashes, int maxCount)
    {
        Wire.WriteListCount(ref writer, hashes.Count, maxCount);
        foreach (ObjectHash hash in hashes)
            hash.Write(ref writer);
    }

    /// <summary>Reads a <c>list&lt;string(maxBytes)&gt;(maxCount)</c>.</summary>
    public static string[] ReadStrings(ref WireReader reader, int maxCount, int maxBytes)
    {
        int count = Wire.ReadListCount(ref reader, maxCount);
        if (!reader.Ok || !Fits(ref reader, count, sizeof(ushort)))
            return [];

        var strings = new string[count];
        for (int i = 0; i < count && reader.Ok; i++)
            strings[i] = Wire.ReadString(ref reader, maxBytes);
        return reader.Ok ? strings : [];
    }

    /// <summary>Writes a <c>list&lt;string(maxBytes)&gt;(maxCount)</c>.</summary>
    public static void WriteStrings(ref WireWriter writer, IReadOnlyList<string> strings, int maxCount, int maxBytes)
    {
        Wire.WriteListCount(ref writer, strings.Count, maxCount);
        foreach (string value in strings)
            Wire.WriteString(ref writer, value, maxBytes);
    }

    /// <summary>A copy of a list a caller hands in, checked against its maximum before anything else happens.</summary>
    public static T[] Own<T>(IReadOnlyList<T> values, int maxCount, string field)
    {
        ArgumentNullException.ThrowIfNull(values, field);
        if (values.Count > maxCount)
            throw new ArgumentException($"The {field} holds {values.Count} elements; at most {maxCount} are allowed (SPEC.md §7).", field);

        var copy = new T[values.Count];
        for (int i = 0; i < copy.Length; i++)
            copy[i] = values[i];
        return copy;
    }

    /// <summary>Whether the bytes left can hold <paramref name="count"/> elements of at least <paramref name="minSize"/> bytes; fails the reader if not.</summary>
    public static bool Fits(ref WireReader reader, int count, int minSize)
    {
        if ((long)count * minSize <= reader.Remaining)
            return true;
        reader.Fail();
        return false;
    }
}

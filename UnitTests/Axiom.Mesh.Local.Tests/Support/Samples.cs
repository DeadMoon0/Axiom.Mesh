using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Axiom.Mesh.Local.Tests.Support;

/// <summary>
/// A sample of every type of the local protocol, built by reflection with every field set to a value that is not its
/// default: a field a codec forgot to read or write changes the bytes of a round trip. Every request, OK body, event
/// and record therefore has a sample without anyone writing one, and a new field is covered the day it is added.
/// </summary>
internal static class Samples
{
    private static readonly Assembly Local = typeof(LocalProtocol).Assembly;

    /// <summary>A sample of <typeparamref name="T"/>.</summary>
    public static T Of<T>() => (T)Of(typeof(T));

    /// <summary>A sample of <paramref name="type"/>.</summary>
    public static object Of(Type type) => Of(type, seed: 1);

    /// <summary>Every request class, by message id.</summary>
    public static IReadOnlyList<Type> RequestTypes() =>
        [.. Local.GetTypes().Where(t => t.IsSubclassOf(typeof(LocalRequest)) && !t.IsAbstract)];

    /// <summary>Every event class.</summary>
    public static IReadOnlyList<Type> EventTypes() =>
        [.. Local.GetTypes().Where(t => t.IsSubclassOf(typeof(LocalEvent)) && !t.IsAbstract)];

    /// <summary>Every record of §5 and every OK body record: the types that read and write themselves and are neither request, event nor callback.</summary>
    public static IReadOnlyList<Type> RecordTypes() =>
        [.. Local.GetExportedTypes().Where(t => !t.IsAbstract && t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IWireRecord<>)))
            .Where(t => !t.IsSubclassOf(typeof(LocalRequest)) && !t.IsSubclassOf(typeof(LocalEvent)) && !t.IsSubclassOf(typeof(LocalCallback)))];

    /// <summary>A 32-byte value whose bytes count up from <paramref name="first"/>.</summary>
    public static Hash32 Hash(int first)
    {
        byte[] bytes = new byte[Hash32.Length];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)(first + i);
        return new Hash32(bytes);
    }

    /// <summary>A response with a sample OK body for the request <paramref name="descriptor"/> names.</summary>
    public static Response OkResponse(RequestDescriptor descriptor, uint correlation)
    {
        MethodInfo ok = typeof(Response).GetMethod(nameof(Response.Ok))!.MakeGenericMethod(descriptor.RequestType, descriptor.OkType);
        return (Response)ok.Invoke(null, [correlation, Of(descriptor.OkType)])!;
    }

    private static object Of(Type type, int seed)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
            return Of(underlying, seed);
        if (type == typeof(bool))
            return true;
        if (type == typeof(byte))
            return (byte)(seed + 6);
        if (type == typeof(ushort))
            return (ushort)(seed + 6);
        if (type == typeof(uint))
            return (uint)(seed + 6);
        if (type == typeof(ulong))
            return (ulong)(seed + 6);
        if (type == typeof(string))
            return "sample-" + seed;
        if (type.IsEnum)
            return FirstNonZero(type);
        if (type == typeof(Hash32))
            return Hash(seed * 3);
        if (type == typeof(UserId))
            return new UserId(Hash(seed * 5));
        if (type == typeof(DeviceId))
            return new DeviceId(Hash(seed * 7));
        if (type == typeof(PackageId))
            return new PackageId(Hash(seed * 11));
        if (type == typeof(ObjectHash))
            return new ObjectHash(Hash(seed * 13));
        if (type == typeof(ServiceToken))
            return ServiceToken.FromBytes(Hash(0xA0).ToArray());
        if (type == typeof(ReadOnlyMemory<byte>))
            return new ReadOnlyMemory<byte>([1, 2, (byte)seed]);
        if (type == typeof(Head))
            return new Head(new DeviceId(Hash(seed + 40)), new ObjectHash(Hash(seed + 41)), (ulong)seed + 9);
        if (type == typeof(Branch))
            return new Branch("main", new ObjectHash(Hash(seed + 50)));
        if (type == typeof(EntryKey))
            return new EntryKey((ulong)seed + 5, new DeviceId(Hash(seed + 60)), new ObjectHash(Hash(seed + 61)));
        if (type == typeof(NoContent))
            return default(NoContent);
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
            return ListOf(type.GetGenericArguments()[0], seed);
        if (type.IsClass)
            return Record(type, seed);
        throw new InvalidOperationException($"No sample for {type}; teach Samples the type.");
    }

    private static Array ListOf(Type element, int seed)
    {
        var list = Array.CreateInstance(element, 1);
        list.SetValue(Of(element, seed + 1), 0);
        return list;
    }

    private static object Record(Type type, int seed)
    {
        object record = Activator.CreateInstance(type, nonPublic: true)
            ?? throw new InvalidOperationException($"{type} cannot be made.");
        int field = 0;
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.SetMethod is null || property.GetIndexParameters().Length > 0)
                continue;
            property.SetValue(record, Of(property.PropertyType, seed + ++field));
        }

        return record;
    }

    private static object FirstNonZero(Type type) =>
        Enum.GetValues(type).Cast<object>().FirstOrDefault(v => Convert.ToUInt64(v, System.Globalization.CultureInfo.InvariantCulture) != 0)
        ?? Enum.ToObject(type, 0);

    private static byte[] ToArray(this Hash32 hash)
    {
        byte[] bytes = new byte[Hash32.Length];
        hash.CopyTo(bytes);
        return bytes;
    }
}

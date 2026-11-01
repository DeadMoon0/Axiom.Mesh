using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using Axiom.Mesh.Local;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Chat;

/// <summary>
/// The payload of a <c>ServiceCall</c>, its reply or a <c>ServiceEvent</c> of <c>axiom-mesh-chat/1</c>: exactly one record
/// in its layout (CHAT-SERVICE.md §4), at most 1 MiB. One encoder for the service and every app.
/// </summary>
public static class ChatCodec
{
    /// <summary>A record's payload.</summary>
    /// <typeparam name="T">The record.</typeparam>
    /// <param name="record">The record.</param>
    /// <returns>Its encoding.</returns>
    /// <exception cref="ArgumentException">A field is outside its bounds, or the encoding exceeds <see cref="ChatLimits.PayloadBytes"/>.</exception>
    public static byte[] Encode<T>(T record)
        where T : class, IWireRecord<T>
    {
        ArgumentNullException.ThrowIfNull(record);
        var output = new ArrayBufferWriter<byte>();
        var writer = new WireWriter(output);
        record.Write(ref writer);
        if (output.WrittenCount > ChatLimits.PayloadBytes)
            throw new ArgumentException($"The {typeof(T).Name} is {output.WrittenCount} bytes; a payload holds at most {ChatLimits.PayloadBytes} (LOCAL-SPEC.md §3.2).", nameof(record));
        return output.WrittenSpan.ToArray();
    }

    /// <summary>The bytes a record's encoding takes, without its 1 MiB check, for a producer that cuts a page to fit.</summary>
    /// <typeparam name="T">The record.</typeparam>
    /// <param name="record">The record.</param>
    /// <returns>Its encoded length.</returns>
    /// <exception cref="ArgumentException">A field is outside its bounds.</exception>
    public static int SizeOf<T>(T record)
        where T : class, IWireRecord<T>
    {
        ArgumentNullException.ThrowIfNull(record);
        var output = new ArrayBufferWriter<byte>();
        var writer = new WireWriter(output);
        record.Write(ref writer);
        return output.WrittenCount;
    }

    /// <summary>Reads a payload that must hold exactly one record of <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The record.</typeparam>
    /// <param name="payload">The payload.</param>
    /// <param name="record">The record, or <see langword="null"/>.</param>
    /// <returns><see langword="false"/> when a field is out of bounds or bytes follow the record.</returns>
    public static bool TryDecode<T>(ReadOnlyMemory<byte> payload, [NotNullWhen(true)] out T? record)
        where T : class, IWireRecord<T>
    {
        var reader = new WireReader(payload);
        if (T.TryRead(ref reader, out record) && reader.Ok && reader.IsAtEnd)
            return true;
        record = null;
        return false;
    }
}

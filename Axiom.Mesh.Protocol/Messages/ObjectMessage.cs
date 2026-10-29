using System;
using Axiom.ProtoStream.Codecs;
using Axiom.ProtoStream.Errors;

namespace Axiom.Mesh.Protocol;

/// <summary>
/// <c>Object</c> (6, SPEC.md §7.6), Established only: one object with its hash.
/// </summary>
/// <remarks>
/// <para>
/// To send, make one with <see cref="From"/>: only a <see cref="StorableObject"/> (hash matched, form parsed, signature
/// verified) may be sent, so nothing unverified is ever forwarded. Its bytes are not copied. An object the sender's own
/// store holds goes out with <see cref="FromStored"/>: the store takes nothing but storable objects, so its bytes were
/// verified when they were stored, and they are neither parsed nor verified again on the way out.
/// </para>
/// <para>
/// A received one is decoded only when its hash equals <c>H(object)</c>; a mismatch is a violation (§7.6). It is a
/// <b>view of its frame</b>: nothing is copied, and it is valid until the next read on the session. Every member throws
/// <see cref="StaleMessageException"/> after that read; parse or copy <see cref="Object"/> before reading on.
/// </para>
/// </remarks>
public sealed class ObjectMessage : MeshMessage, IWireMessage<ObjectMessage>
{
    private readonly HashedObject received;
    private readonly StorableObject? sent;
    private readonly Stored? stored;
    private readonly MessageStamp stamp;

    private ObjectMessage(StorableObject sent)
    {
        this.sent = sent;
    }

    private ObjectMessage(Stored stored)
    {
        this.stored = stored;
    }

    private ObjectMessage(HashedObject received, MessageStamp stamp)
    {
        this.received = received;
        this.stamp = stamp;
    }

    /// <inheritdoc />
    public override ushort Id => MessageIds.Object;

    /// <summary>The object's name.</summary>
    /// <exception cref="StaleMessageException">The session has read past this message.</exception>
    public ObjectHash Hash
    {
        get
        {
            stamp.ThrowIfStale();
            return stored?.Hash ?? sent?.Hash ?? received.Hash;
        }
    }

    /// <summary>Every byte of the object; for a received message, a view of the frame.</summary>
    /// <exception cref="StaleMessageException">The session has read past this message.</exception>
    public ReadOnlyMemory<byte> Bytes
    {
        get
        {
            stamp.ThrowIfStale();
            return stored?.Bytes ?? sent?.Bytes ?? received.Bytes;
        }
    }

    /// <summary>
    /// The object with its proven hash, ready for <see cref="Objects.TryParse"/>. For a received message the hash was
    /// verified when the frame was decoded; for one made with <see cref="From"/> it is hashed again on every call, which a
    /// sender never needs.
    /// </summary>
    /// <exception cref="StaleMessageException">The session has read past this message.</exception>
    public HashedObject Object
    {
        get
        {
            stamp.ThrowIfStale();
            return stored is not null ? HashedObject.Of(stored.Bytes) : sent is null ? received : HashedObject.Of(sent.Bytes);
        }
    }

    /// <summary>Whether this message still refers to valid memory: always for a sent one, until the next read for a received one.</summary>
    public bool IsCurrent => stamp.IsCurrent;

    /// <summary>The message that sends a storable object (SPEC.md §7.6).</summary>
    /// <param name="storable">The object; its bytes must stay unchanged until the message was written.</param>
    /// <returns>The message.</returns>
    public static ObjectMessage From(StorableObject storable)
    {
        ArgumentNullException.ThrowIfNull(storable);
        return new ObjectMessage(storable);
    }

    /// <summary>
    /// The message that sends an object read back from the sender's own store (SPEC.md §7.6, §11.1): the store holds only
    /// objects that were storable when they were stored (hash matched, form parsed, signature verified, Entry admitted), and
    /// a stored Entry stays valid and forwardable whatever later Rosters and Grants say (§9.6), so its bytes go out as they
    /// are, without hashing, parsing or verifying them again.
    /// </summary>
    /// <param name="hash">The object's name, as the store files it.</param>
    /// <param name="bytes">Every byte of the object, read from the store; they must stay unchanged until the message was written.</param>
    /// <returns>The message.</returns>
    /// <remarks>
    /// Only the store's send path may call this; bytes from anywhere else go through <see cref="From"/>. Axiom.Mesh.Core
    /// keeps that rule with a convention test, since a type cannot say where bytes came from.
    /// </remarks>
    /// <exception cref="ArgumentException">More bytes than an object may hold.</exception>
    public static ObjectMessage FromStored(ObjectHash hash, ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length > Limits.MaxObjectLength)
            throw new ArgumentException($"An object holds at most {Limits.MaxObjectLength} bytes (SPEC.md §8.1); these are {bytes.Length}.", nameof(bytes));
        return new ObjectMessage(new Stored(hash, bytes));
    }

    /// <summary>
    /// Reads an <c>Object</c> body as a view of the reader's memory with no stamp: the caller keeps that memory alive. The
    /// session decodes through <see cref="Amp.Definition"/>, which stamps the message to its frame.
    /// </summary>
    /// <param name="reader">The reader.</param>
    /// <param name="message">The message.</param>
    /// <returns><see langword="true"/> when the body is well-formed and its hash matches.</returns>
    public static bool TryRead(ref WireReader reader, out ObjectMessage message) => TryRead(ref reader, default, out message);

    /// <summary>Reads an <c>Object</c> body as a view of a frame, stamped so that it refuses use after the next read.</summary>
    internal static bool TryRead(ref WireReader reader, MessageStamp stamp, out ObjectMessage message)
    {
        ObjectHash claimed = ObjectHash.Read(ref reader);
        ReadOnlyMemory<byte> bytes = Wire.ReadBlob(ref reader, Limits.MaxObjectLength);
        // The length was checked against 4 MiB before any content was read; the hash is checked before anything acts on it.
        HashedObject hashed = default;
        if (reader.Ok && !HashedObject.TryVerify(bytes, claimed, out hashed))
            reader.Fail();

        message = new ObjectMessage(hashed, stamp);
        return reader.Ok;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        Hash.Write(ref writer);
        Wire.WriteBlob(ref writer, Bytes.Span, Limits.MaxObjectLength);
    }

    // An object read back from the store: kept apart, so a received message stays as small as it was.
    private sealed record Stored(ObjectHash Hash, ReadOnlyMemory<byte> Bytes);

    /// <inheritdoc />
    public override string ToString() => stamp.IsCurrent ? $"Object {Hash} ({Bytes.Length} bytes)" : "Object (stale)";
}

using System;
using System.Buffers.Binary;

namespace Axiom.Mesh.Discovery;

/// <summary>
/// The UDP broadcast datagram of SPEC.md §3.2, exactly:
/// <c>magic "AMD1" (4) ‖ flags u8 (bit 0: announcement; bits 1–7 zero) ‖ deviceId (32) ‖ userId (32) ‖ port u16 big-endian</c>,
/// 71 bytes. Pure: no socket, no clock. Nothing in it is authenticated; it is a hint like a DNS-SD record.
/// </summary>
public static class BroadcastCodec
{
    /// <summary>The UDP port nodes broadcast and listen on (SPEC.md §3.2, §14.9).</summary>
    public const int Port = 41811;

    /// <summary>The only length a datagram may have: 4 + 1 + 32 + 32 + 2 bytes.</summary>
    public const int Length = 71;

    private const int FlagsOffset = 4;
    private const int DeviceOffset = 5;
    private const int UserOffset = DeviceOffset + Hash32.Length;
    private const int PortOffset = UserOffset + Hash32.Length;
    private const byte AnnouncementBit = 0x01;

    /// <summary>The four magic bytes, ASCII <c>AMD1</c>; the version lives in the magic.</summary>
    public static ReadOnlySpan<byte> Magic => "AMD1"u8;

    /// <summary>
    /// Reads a datagram. Refuses (SPEC.md §3.2: MUST ignore) any other length, another magic or a flag bit other than
    /// bit 0; also refuses an all-zero id, which no key hashes to and which only a static peer claims, and port 0,
    /// which cannot be dialled. Never throws.
    /// </summary>
    /// <param name="bytes">The datagram as received.</param>
    /// <param name="datagram">The datagram, or <c>default</c>.</param>
    /// <returns><see langword="true"/> when the datagram is well-formed.</returns>
    public static bool TryParse(ReadOnlySpan<byte> bytes, out BroadcastDatagram datagram)
    {
        datagram = default;
        if (bytes.Length != Length || !bytes[..FlagsOffset].SequenceEqual(Magic))
            return false;

        byte flags = bytes[FlagsOffset];
        if ((flags & ~AnnouncementBit) != 0)
            return false;

        var device = new DeviceId(new Hash32(bytes.Slice(DeviceOffset, Hash32.Length)));
        var user = new UserId(new Hash32(bytes.Slice(UserOffset, Hash32.Length)));
        ushort port = BinaryPrimitives.ReadUInt16BigEndian(bytes[PortOffset..]);
        if (device.Value.IsZero || user.Value.IsZero || port == 0)
            return false;

        datagram = new BroadcastDatagram((flags & AnnouncementBit) != 0 ? BroadcastKind.Announcement : BroadcastKind.Query, device, user, port);
        return true;
    }

    /// <summary>Writes a query or an announcement carrying this node's ids and port.</summary>
    /// <param name="kind">Query or announcement.</param>
    /// <param name="self">This node.</param>
    /// <param name="destination">At least <see cref="Length"/> bytes.</param>
    /// <returns><see cref="Length"/>, the bytes written.</returns>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is shorter than <see cref="Length"/>, or <paramref name="kind"/> is undefined.</exception>
    public static int Write(BroadcastKind kind, SelfRecord self, Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(self);
        if (destination.Length < Length)
            throw new ArgumentException($"A broadcast datagram is {Length} bytes; the destination holds {destination.Length}.", nameof(destination));
        if (kind is not (BroadcastKind.Query or BroadcastKind.Announcement))
            throw new ArgumentException($"{kind} is neither a query nor an announcement.", nameof(kind));

        Magic.CopyTo(destination);
        destination[FlagsOffset] = kind == BroadcastKind.Announcement ? AnnouncementBit : (byte)0;
        self.Device.Value.CopyTo(destination[DeviceOffset..]);
        self.User.Value.CopyTo(destination[UserOffset..]);
        BinaryPrimitives.WriteUInt16BigEndian(destination[PortOffset..], self.Port);
        return Length;
    }

    /// <summary>A new array holding a query or an announcement of this node.</summary>
    /// <param name="kind">Query or announcement.</param>
    /// <param name="self">This node.</param>
    /// <returns>The <see cref="Length"/> bytes.</returns>
    public static byte[] Write(BroadcastKind kind, SelfRecord self)
    {
        byte[] datagram = new byte[Length];
        Write(kind, self, datagram);
        return datagram;
    }
}

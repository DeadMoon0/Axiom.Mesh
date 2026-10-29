using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace Axiom.Mesh.Discovery;

/// <summary>
/// The DNS-SD form of a node's announcement (SPEC.md §3.1), hand-rolled over RFC 1035 messages as mDNS (RFC 6762)
/// carries them: the answer a node multicasts, the query it sends, and the reading of a received packet. Pure: no
/// socket, no clock. A record is a hint, never trusted; only the TLS handshake proves who answers.
/// </summary>
/// <remarks>
/// <para>
/// A node announces the service <c>_axiom-mesh._tcp.local</c> with the instance <c>&lt;first 32 hex of the device
/// id&gt;</c>: a PTR from the service to the instance, an SRV naming the mesh port and the host
/// <c>&lt;instance&gt;.local</c>, a TXT with <c>v=1</c>, <c>d=&lt;device id&gt;</c> and <c>u=&lt;user id&gt;</c>, and
/// an A or AAAA record per address of the interface it answers on. The host name is the instance, never the machine's
/// name, so it cannot clash with the records the operating system's own responder publishes.
/// </para>
/// <para>
/// Reading is bounded and never throws: a packet longer than <see cref="MaxPacketLength"/>, with more than
/// <see cref="MaxRecords"/> records, a non-zero opcode or response code, a name over 255 characters, a compression
/// pointer that does not point backwards, or any byte beyond the last record is refused as a whole. Within a
/// well-formed packet, an instance counts only with an SRV and a TXT of class IN and a non-zero TTL whose <c>v</c> is
/// <c>1</c> and whose <c>d</c> and <c>u</c> are 64 hex digits, <c>d</c> starting with the instance name (SPEC.md §3.1).
/// </para>
/// </remarks>
public sealed class DnsSdCodec
{
    /// <summary>The DNS-SD service type every node announces (SPEC.md §3.1).</summary>
    public const string ServiceType = "_axiom-mesh._tcp";

    /// <summary>The domain mDNS serves (RFC 6762 §3).</summary>
    public const string Domain = "local";

    /// <summary>The UDP port of mDNS (RFC 6762).</summary>
    public const int Port = 5353;

    /// <summary>The IPv4 mDNS group (RFC 6762 §3).</summary>
    public const string IPv4Group = "224.0.0.251";

    /// <summary>The IPv6 mDNS group, link-local scope (RFC 6762 §3).</summary>
    public const string IPv6Group = "ff02::fb";

    /// <summary>The longest packet read or written: 9000 bytes (RFC 6762 §17).</summary>
    public const int MaxPacketLength = 9000;

    /// <summary>The most questions and records a packet may hold; a responder of one node sends a handful.</summary>
    public const int MaxRecords = 64;

    /// <summary>The most instances one packet may announce; further ones are ignored.</summary>
    public const int MaxInstances = 8;

    /// <summary>The most addresses an answer carries.</summary>
    public const int MaxAddresses = 16;

    /// <summary>The length of an instance name: the first 32 hex characters of the device id (SPEC.md §3.1).</summary>
    public const int InstanceNameLength = 32;

    private const int HeaderLength = 12;
    private const int MaxNameLength = 255;
    private const int MaxLabelLength = 63;
    private const int MaxPointerJumps = 16;
    private const int IdHexLength = 2 * Hash32.Length;

    private const ushort ResponseFlags = 0x8400;
    private const ushort TypeA = 1;
    private const ushort TypePtr = 12;
    private const ushort TypeTxt = 16;
    private const ushort TypeAaaa = 28;
    private const ushort TypeSrv = 33;
    private const ushort TypeAny = 255;
    private const ushort ClassIn = 1;
    private const ushort ClassAny = 255;
    private const ushort CacheFlush = 0x8000;

    // RFC 6762 §10: 120 s for records that name a host, 75 minutes for the rest.
    private const uint HostTtl = 120;
    private const uint OtherTtl = 4500;

    private DnsSdCodec()
    {
    }

    /// <summary>The instance name of a device: the first 32 of the 64 lowercase hex characters of its id (SPEC.md §3.1).</summary>
    /// <param name="device">The device id.</param>
    /// <returns>32 lowercase hex characters.</returns>
    public static string InstanceName(DeviceId device) => device.Value.ToHex()[..InstanceNameLength];

    /// <summary>The query a node multicasts to find the others: one question, PTR of the service type, class IN.</summary>
    /// <returns>The packet.</returns>
    public static byte[] WriteQuery()
    {
        var packet = new PacketWriter();
        packet.Header(flags: 0, questions: 1, answers: 0);
        packet.Name(ServiceLabels());
        packet.UInt16(TypePtr);
        packet.UInt16(ClassIn);
        return packet.ToArray();
    }

    /// <summary>
    /// The answer a node multicasts to announce itself: PTR, SRV, TXT, and one A or AAAA record per address, each with
    /// the TTL RFC 6762 §10 recommends and the cache-flush bit on the records only this node owns.
    /// </summary>
    /// <param name="self">This node.</param>
    /// <param name="addresses">The addresses of the interface the answer leaves on; IPv4 or IPv6, at most <see cref="MaxAddresses"/>.</param>
    /// <returns>The packet.</returns>
    /// <exception cref="ArgumentException">Too many addresses, or one of another family.</exception>
    public static byte[] WriteAnswer(SelfRecord self, IReadOnlyList<IPAddress> addresses)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(addresses);
        if (addresses.Count > MaxAddresses)
            throw new ArgumentException($"An answer carries at most {MaxAddresses} addresses; {addresses.Count} were given.", nameof(addresses));

        string instance = InstanceName(self.Device);
        string[] service = ServiceLabels();
        string[] instanceName = [instance, .. service];
        string[] hostName = [instance, Domain];

        var packet = new PacketWriter();
        packet.Header(ResponseFlags, questions: 0, answers: (ushort)(3 + addresses.Count));

        packet.Name(service);
        packet.Record(TypePtr, ClassIn, OtherTtl);
        int ptr = packet.BeginData();
        packet.Name(instanceName);
        packet.EndData(ptr);

        packet.Name(instanceName);
        packet.Record(TypeSrv, ClassIn | CacheFlush, HostTtl);
        int srv = packet.BeginData();
        packet.UInt16(0);
        packet.UInt16(0);
        packet.UInt16(self.Port);
        packet.Name(hostName);
        packet.EndData(srv);

        packet.Name(instanceName);
        packet.Record(TypeTxt, ClassIn | CacheFlush, OtherTtl);
        int txt = packet.BeginData();
        packet.Text("v=1");
        packet.Text("d=" + self.Device.Value.ToHex());
        packet.Text("u=" + self.User.Value.ToHex());
        packet.EndData(txt);

        foreach (IPAddress address in addresses)
        {
            ushort type = address.AddressFamily switch
            {
                AddressFamily.InterNetwork => TypeA,
                AddressFamily.InterNetworkV6 => TypeAaaa,
                _ => throw new ArgumentException($"{address} is neither IPv4 nor IPv6.", nameof(addresses)),
            };
            packet.Name(hostName);
            packet.Record(type, ClassIn | CacheFlush, HostTtl);
            int data = packet.BeginData();
            packet.Bytes(address.GetAddressBytes());
            packet.EndData(data);
        }

        return packet.ToArray();
    }

    /// <summary>Whether a packet is an mDNS query asking for the service type (PTR or ANY, class IN or ANY). Never throws.</summary>
    /// <param name="packet">The packet as received.</param>
    /// <returns><see langword="true"/> when a node should answer it.</returns>
    public static bool IsServiceQuery(ReadOnlySpan<byte> packet)
    {
        if (!TryReadHeader(packet, out Header header) || header.IsResponse)
            return false;

        Span<char> name = stackalloc char[MaxNameLength];
        int position = HeaderLength;
        bool asked = false;
        for (int i = 0; i < header.Questions; i++)
        {
            if (!TryReadName(packet, ref position, name, out int length) || position + 4 > packet.Length)
                return false;

            ushort type = BinaryPrimitives.ReadUInt16BigEndian(packet[position..]);
            ushort klass = (ushort)(BinaryPrimitives.ReadUInt16BigEndian(packet[(position + 2)..]) & 0x7FFF);
            position += 4;
            asked |= name[..length].SequenceEqual(ServiceName) && type is TypePtr or TypeAny && klass is ClassIn or ClassAny;
        }

        return asked && SkipRecords(packet, ref position, header.Records) && position == packet.Length;
    }

    /// <summary>
    /// The announcements an mDNS response carries for the service type (SPEC.md §3.1), each at
    /// <paramref name="source"/> with the port its SRV names. Empty when the packet is malformed, a query, or names no
    /// well-formed instance. Never throws.
    /// </summary>
    /// <param name="packet">The packet as received.</param>
    /// <param name="source">The packet's source address: the address a dialler can reach on that link.</param>
    /// <returns>At most <see cref="MaxInstances"/> announcements of <see cref="DiscoverySource.Mdns"/>.</returns>
    public static IReadOnlyList<Announcement> ReadAnnouncements(ReadOnlySpan<byte> packet, IPAddress source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!TryReadHeader(packet, out Header header) || !header.IsResponse)
            return [];

        Span<char> name = stackalloc char[MaxNameLength];
        int position = HeaderLength;
        for (int i = 0; i < header.Questions; i++)
        {
            if (!TryReadName(packet, ref position, name, out _) || (position += 4) > packet.Length)
                return [];
        }

        var instances = new List<Instance>();
        for (int i = 0; i < header.Records; i++)
        {
            if (!TryReadName(packet, ref position, name, out int length) || !TryReadRecordHeader(packet, ref position, out Record record))
                return [];

            ReadOnlySpan<byte> data = packet.Slice(position, record.DataLength);
            int dataStart = position;
            position += record.DataLength;
            if (record.Ttl == 0 || record.Class != ClassIn || record.Type is not (TypeSrv or TypeTxt))
                continue;
            if (!TryInstanceLabel(name[..length], out ReadOnlySpan<char> label))
                continue;

            Instance? instance = Find(instances, label);
            if (instance is null)
            {
                if (instances.Count == MaxInstances)
                    continue;
                instance = new Instance(new string(label));
                instances.Add(instance);
            }

            if (record.Type == TypeSrv)
            {
                if (!instance.HasPort && !TryReadSrvPort(packet, dataStart, record.DataLength, out instance.Port))
                    return [];
                instance.HasPort = true;
            }
            else if (!instance.HasText)
            {
                if (!TryReadText(data, instance))
                    return [];
                instance.HasText = true;
            }
        }

        if (position != packet.Length)
            return [];

        var announcements = new List<Announcement>(instances.Count);
        foreach (Instance instance in instances)
        {
            if (instance.IsAnnouncement)
                announcements.Add(new Announcement(instance.Device, instance.User, new IPEndPoint(source, instance.Port), DiscoverySource.Mdns));
        }

        return announcements;
    }

    private static ReadOnlySpan<char> ServiceName => "_axiom-mesh._tcp.local";

    private static string[] ServiceLabels() => [.. ServiceType.Split('.'), Domain];

    private static Instance? Find(List<Instance> instances, ReadOnlySpan<char> label)
    {
        foreach (Instance instance in instances)
        {
            if (label.SequenceEqual(instance.Label))
                return instance;
        }

        return null;
    }

    // "<32 lowercase hex>._axiom-mesh._tcp.local": one label before the service name, of the instance name's form.
    private static bool TryInstanceLabel(ReadOnlySpan<char> name, out ReadOnlySpan<char> label)
    {
        label = default;
        if (name.Length != InstanceNameLength + 1 + ServiceName.Length || name[InstanceNameLength] != '.' || !name[(InstanceNameLength + 1)..].SequenceEqual(ServiceName))
            return false;

        label = name[..InstanceNameLength];
        foreach (char c in label)
        {
            if (!char.IsAsciiHexDigitLower(c))
                return false;
        }

        return true;
    }

    private static bool TryReadHeader(ReadOnlySpan<byte> packet, out Header header)
    {
        header = default;
        if (packet.Length < HeaderLength || packet.Length > MaxPacketLength)
            return false;

        ushort flags = BinaryPrimitives.ReadUInt16BigEndian(packet[2..]);
        int questions = BinaryPrimitives.ReadUInt16BigEndian(packet[4..]);
        int records = BinaryPrimitives.ReadUInt16BigEndian(packet[6..]) + BinaryPrimitives.ReadUInt16BigEndian(packet[8..]) + BinaryPrimitives.ReadUInt16BigEndian(packet[10..]);

        // RFC 6762 §18.3, §18.11: a non-zero opcode or response code is silently ignored.
        if (((flags >> 11) & 0xF) != 0 || (flags & 0xF) != 0 || questions + records > MaxRecords)
            return false;

        header = new Header((flags & 0x8000) != 0, questions, records);
        return true;
    }

    private static bool TryReadRecordHeader(ReadOnlySpan<byte> packet, ref int position, out Record record)
    {
        record = default;
        if (position + 10 > packet.Length)
            return false;

        ushort type = BinaryPrimitives.ReadUInt16BigEndian(packet[position..]);
        ushort klass = (ushort)(BinaryPrimitives.ReadUInt16BigEndian(packet[(position + 2)..]) & 0x7FFF);
        uint ttl = BinaryPrimitives.ReadUInt32BigEndian(packet[(position + 4)..]);
        int dataLength = BinaryPrimitives.ReadUInt16BigEndian(packet[(position + 8)..]);
        position += 10;
        if (position + dataLength > packet.Length)
            return false;

        record = new Record(type, klass, ttl, dataLength);
        return true;
    }

    private static bool SkipRecords(ReadOnlySpan<byte> packet, ref int position, int count)
    {
        Span<char> name = stackalloc char[MaxNameLength];
        for (int i = 0; i < count; i++)
        {
            if (!TryReadName(packet, ref position, name, out _) || !TryReadRecordHeader(packet, ref position, out Record record))
                return false;
            position += record.DataLength;
        }

        return true;
    }

    // SRV data: priority u16, weight u16, port u16, target name (which may be compressed and must stay in the data).
    private static bool TryReadSrvPort(ReadOnlySpan<byte> packet, int dataStart, int dataLength, out ushort port)
    {
        port = 0;
        if (dataLength < 7)
            return false;

        port = BinaryPrimitives.ReadUInt16BigEndian(packet[(dataStart + 4)..]);
        int target = dataStart + 6;
        Span<char> name = stackalloc char[MaxNameLength];
        return TryReadName(packet[..(dataStart + dataLength)], ref target, name, out _) && target == dataStart + dataLength;
    }

    // TXT data: length-prefixed strings "key=value"; keys compare without case, the first of a key counts (RFC 6763 §6.4).
    private static bool TryReadText(ReadOnlySpan<byte> data, Instance instance)
    {
        int position = 0;
        while (position < data.Length)
        {
            int length = data[position++];
            if (position + length > data.Length)
                return false;

            ReadOnlySpan<byte> entry = data.Slice(position, length);
            position += length;
            int equals = entry.IndexOf((byte)'=');
            ReadOnlySpan<byte> key = equals < 0 ? entry : entry[..equals];
            ReadOnlySpan<byte> value = equals < 0 ? default : entry[(equals + 1)..];
            if (key.Length != 1)
                continue;

            switch (key[0] | 0x20)
            {
                case 'v' when instance.Version == Field.Absent:
                    instance.Version = value.SequenceEqual("1"u8) ? Field.Valid : Field.Invalid;
                    break;
                case 'd' when instance.DeviceField == Field.Absent:
                    instance.DeviceField = TryParseId(value, out Hash32 device) ? Field.Valid : Field.Invalid;
                    instance.Device = new DeviceId(device);
                    break;
                case 'u' when instance.UserField == Field.Absent:
                    instance.UserField = TryParseId(value, out Hash32 user) ? Field.Valid : Field.Invalid;
                    instance.User = new UserId(user);
                    break;
            }
        }

        return true;
    }

    private static bool TryParseId(ReadOnlySpan<byte> value, out Hash32 id)
    {
        id = default;
        if (value.Length != IdHexLength)
            return false;

        Span<char> hex = stackalloc char[IdHexLength];
        for (int i = 0; i < IdHexLength; i++)
            hex[i] = (char)value[i];
        return Hash32.TryParseHex(hex, out id) && !id.IsZero;
    }

    /// <summary>
    /// Reads a possibly compressed name (RFC 1035 §4.1.4) as lowercase dotted text. A pointer must point before itself,
    /// at most <see cref="MaxPointerJumps"/> are followed, and the text stays within <see cref="MaxNameLength"/>.
    /// </summary>
    private static bool TryReadName(ReadOnlySpan<byte> packet, ref int position, Span<char> name, out int length)
    {
        length = 0;
        int cursor = position;
        int after = -1;
        int jumps = 0;
        while (true)
        {
            if (cursor >= packet.Length)
                return false;

            int label = packet[cursor];
            if (label == 0)
            {
                position = after >= 0 ? after : cursor + 1;
                return true;
            }

            if ((label & 0xC0) == 0xC0)
            {
                if (cursor + 1 >= packet.Length || ++jumps > MaxPointerJumps)
                    return false;

                int target = ((label & 0x3F) << 8) | packet[cursor + 1];
                if (target >= cursor)
                    return false;
                if (after < 0)
                    after = cursor + 2;
                cursor = target;
                continue;
            }

            if ((label & 0xC0) != 0 || cursor + 1 + label > packet.Length || length + (length > 0 ? 1 : 0) + label > name.Length)
                return false;

            if (length > 0)
                name[length++] = '.';
            foreach (byte b in packet.Slice(cursor + 1, label))
                name[length++] = b is >= (byte)'A' and <= (byte)'Z' ? (char)(b | 0x20) : (char)b;
            cursor += 1 + label;
        }
    }

    private enum Field
    {
        Absent = 0,
        Valid = 1,
        Invalid = 2,
    }

    private readonly record struct Header(bool IsResponse, int Questions, int Records);

    private readonly record struct Record(ushort Type, ushort Class, uint Ttl, int DataLength);

    private sealed class Instance(string label)
    {
        public ushort Port;

        public string Label { get; } = label;

        public bool HasPort { get; set; }

        public bool HasText { get; set; }

        public Field Version { get; set; }

        public Field DeviceField { get; set; }

        public Field UserField { get; set; }

        public DeviceId Device { get; set; }

        public UserId User { get; set; }

        // SPEC.md §3.1: v must be 1 and d must start with the instance name; and a dialler needs a port and both ids.
        public bool IsAnnouncement =>
            HasPort && Port != 0 && HasText
            && Version == Field.Valid && DeviceField == Field.Valid && UserField == Field.Valid
            && Device.Value.ToHex().AsSpan(0, InstanceNameLength).SequenceEqual(Label);
    }

    private sealed class PacketWriter
    {
        private readonly byte[] buffer = new byte[MaxPacketLength];
        private int length;

        public void Header(ushort flags, ushort questions, ushort answers)
        {
            UInt16(0);
            UInt16(flags);
            UInt16(questions);
            UInt16(answers);
            UInt16(0);
            UInt16(0);
        }

        public void Name(string[] labels)
        {
            foreach (string label in labels)
            {
                if (label.Length is 0 or > MaxLabelLength)
                    throw new ArgumentException($"The DNS label '{label}' is empty or longer than {MaxLabelLength} characters.", nameof(labels));
                buffer[length++] = (byte)label.Length;
                foreach (char c in label)
                    buffer[length++] = checked((byte)c);
            }

            buffer[length++] = 0;
        }

        public void Record(ushort type, int klass, uint ttl)
        {
            UInt16(type);
            UInt16((ushort)klass);
            BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(length), ttl);
            length += 4;
        }

        public int BeginData()
        {
            length += 2;
            return length;
        }

        public void EndData(int start) => BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(start - 2), (ushort)(length - start));

        public void Text(string text)
        {
            buffer[length++] = checked((byte)text.Length);
            foreach (char c in text)
                buffer[length++] = checked((byte)c);
        }

        public void Bytes(byte[] bytes)
        {
            bytes.CopyTo(buffer, length);
            length += bytes.Length;
        }

        public void UInt16(ushort value)
        {
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(length), value);
            length += 2;
        }

        public byte[] ToArray() => buffer.AsSpan(0, length).ToArray();
    }
}

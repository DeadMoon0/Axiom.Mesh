using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace Axiom.Mesh.Discovery.Tests.Support;

/// <summary>
/// Builds mDNS responses by hand, independently of the codec, so the rules are tested against packets the codec did
/// not write: an SRV and a TXT for one instance of <c>_axiom-mesh._tcp.local</c>, no compression.
/// </summary>
internal static class DnsPacket
{
    public static byte[] Response(string instanceLabel, ushort port, params string[] txt)
    {
        var bytes = new List<byte>();
        UInt16(bytes, 0);
        UInt16(bytes, 0x8400);
        UInt16(bytes, 0);
        UInt16(bytes, 2);
        UInt16(bytes, 0);
        UInt16(bytes, 0);

        string[] owner = [instanceLabel, "_axiom-mesh", "_tcp", "local"];
        var srv = new List<byte>();
        UInt16(srv, 0);
        UInt16(srv, 0);
        UInt16(srv, port);
        Name(srv, instanceLabel, "local");
        Record(bytes, owner, 33, srv);

        var text = new List<byte>();
        foreach (string entry in txt)
        {
            byte[] encoded = Encoding.ASCII.GetBytes(entry);
            text.Add((byte)encoded.Length);
            text.AddRange(encoded);
        }

        Record(bytes, owner, 16, text);
        return [.. bytes];
    }

    private static void Record(List<byte> bytes, string[] owner, ushort type, List<byte> data)
    {
        Name(bytes, owner);
        UInt16(bytes, type);
        UInt16(bytes, 0x8001);
        UInt16(bytes, 0);
        UInt16(bytes, 120);
        UInt16(bytes, (ushort)data.Count);
        bytes.AddRange(data);
    }

    private static void Name(List<byte> bytes, params string[] labels)
    {
        foreach (string label in labels)
        {
            bytes.Add((byte)label.Length);
            bytes.AddRange(Encoding.ASCII.GetBytes(label));
        }

        bytes.Add(0);
    }

    private static void UInt16(List<byte> bytes, ushort value)
    {
        byte[] two = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(two, value);
        bytes.AddRange(two);
    }
}

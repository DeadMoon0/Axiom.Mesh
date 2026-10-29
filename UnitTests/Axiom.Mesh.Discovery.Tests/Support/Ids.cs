using System;
using System.Linq;

namespace Axiom.Mesh.Discovery.Tests.Support;

/// <summary>Readable ids for tests: 32 equal bytes, or the bytes 1 to 32.</summary>
internal static class Ids
{
    public static DeviceId Device(byte fill) => new(Filled(fill));

    public static UserId User(byte fill) => new(Filled(fill));

    /// <summary>The device id 0102…20, whose instance name is <c>0102030405060708090a0b0c0d0e0f10</c>.</summary>
    public static DeviceId CountingDevice() => new(new Hash32([.. Enumerable.Range(1, 32).Select(i => (byte)i)]));

    /// <summary>A device id made of a running number, for many distinct devices.</summary>
    public static DeviceId Numbered(int number)
    {
        byte[] bytes = new byte[32];
        BitConverter.TryWriteBytes(bytes.AsSpan(28), number);
        bytes[0] = 0x5A;
        return new DeviceId(new Hash32(bytes));
    }

    public static SelfRecord Self(byte device, byte user, ushort port) => new() { Device = Device(device), User = User(user), Port = port };

    private static Hash32 Filled(byte fill) => new([.. Enumerable.Repeat(fill, 32)]);
}

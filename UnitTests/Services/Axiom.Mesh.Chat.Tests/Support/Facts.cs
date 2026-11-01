using System.Collections.Generic;
using System.Linq;
using Axiom.Mesh.Local;
using Axiom.Mesh.Payloads;

namespace Axiom.Mesh.Chat.Tests.Support;

/// <summary>Fixed ids, keys and records for the rule tests: every value chosen, nothing random.</summary>
internal static class Facts
{
    public static PackageId Family { get; } = new(Id(0xc4));

    public static PackageId Other { get; } = new(Id(0xc5));

    public static UserId Alice { get; } = new(Id(0xa1));

    public static UserId Bob { get; } = new(Id(0xb0));

    public static DeviceId Laptop { get; } = new(Id(0x1a));

    public static DeviceId Phone { get; } = new(Id(0x1b));

    public static DeviceId Desktop { get; } = new(Id(0xd3));

    public static Hash32 Id(byte first) => new([first, .. Enumerable.Range(1, 31).Select(i => (byte)(first ^ i))]);

    public static ObjectHash Hash(byte first) => new(Id(first));

    public static EntryKey Key(ulong clock, DeviceId device, byte hash) => new(clock, device, Hash(hash));

    public static MessageView View(EntryKey key, string text) => new() { Key = key, PackageId = Family, Author = new AuthorView { DeviceId = key.Device }, Text = text };

    public static IReadOnlyList<Device> Devices { get; } =
    [
        new Device { DeviceId = Laptop, UserId = Alice, Name = "laptop" },
        new Device { DeviceId = Phone, UserId = Alice, Name = "phone", Removed = true },
        new Device { DeviceId = Desktop, UserId = Bob, Name = "desktop" },
    ];

    public static IReadOnlyList<User> Users { get; } = [new User { UserId = Alice, Name = "alice" }, new User { UserId = Bob, Name = "bob" }];

    public static Attachment Attachment(string name, byte hash, ulong size) => new(name, Hash(hash), size);
}

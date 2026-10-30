using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

namespace Axiom.Mesh.Testing;

/// <summary>
/// One published test device with its user (<see cref="TestKeys"/>): <c>alice-laptop</c>, <c>alice-phone</c>,
/// <c>bob-desktop</c> and <c>eve-device</c>. A node of a <see cref="TestMesh"/> is named after one, and its user's
/// Roster lists every published device of that user.
/// </summary>
public sealed class TestIdentity
{
    /// <summary>The time every test Roster says its devices were added: the mesh clock's start.</summary>
    public static DateTimeOffset Epoch { get; } = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    private TestIdentity(string name, string userName, EcdsaSigner user, EcdsaSigner device, string devicePkcs8, string userPkcs8)
    {
        Name = name;
        UserName = userName;
        User = user;
        Device = device;
        this.devicePkcs8 = devicePkcs8;
        this.userPkcs8 = userPkcs8;
    }

    private readonly string devicePkcs8;
    private readonly string userPkcs8;

    /// <summary>Every published device, in the order of <see cref="TestKeys.All"/>.</summary>
    public static IReadOnlyList<TestIdentity> All { get; } = Build();

    private static IReadOnlyDictionary<string, VerifiedRoster> FirstRosters { get; } =
        All.Select(i => i.UserName).Distinct().ToFrozenDictionary(u => u, u => RosterOf(u, 1));

    /// <summary>The device's published name, for example <c>alice-laptop</c>.</summary>
    public string Name { get; }

    /// <summary>The user's published name, for example <c>alice</c>.</summary>
    public string UserName { get; }

    /// <summary>The user key.</summary>
    public EcdsaSigner User { get; }

    /// <summary>The device key.</summary>
    public EcdsaSigner Device { get; }

    /// <summary>The device id.</summary>
    public DeviceId DeviceId => Device.PublicKey.AsDeviceId();

    /// <summary>The user id.</summary>
    public UserId UserId => User.PublicKey.AsUserId();

    /// <summary>The device key as PKCS#8, a new copy each time, as a key store holds it.</summary>
    /// <returns>The bytes.</returns>
    public byte[] DevicePkcs8() => Convert.FromHexString(devicePkcs8);

    /// <summary>The user key as PKCS#8, a new copy each time: what the key store of the device that holds it keeps.</summary>
    /// <returns>The bytes.</returns>
    public byte[] UserPkcs8() => Convert.FromHexString(userPkcs8);

    /// <summary>The published device of that name.</summary>
    /// <param name="name">A device name: <c>alice-laptop</c>, <c>alice-phone</c>, <c>bob-desktop</c> or <c>eve-device</c>.</param>
    /// <returns>The identity.</returns>
    /// <exception cref="ArgumentException">No published device has that name.</exception>
    public static TestIdentity Of(string name) =>
        All.FirstOrDefault(i => i.Name == name)
        ?? throw new ArgumentException($"No published test device is called '{name}'; the devices are {string.Join(", ", All.Select(i => i.Name))}.", nameof(name));

    /// <summary>
    /// The first Roster (seq 1) of a published user, listing every published device of that user, none removed: the one
    /// a <see cref="TestMesh"/> gives its nodes. Always the same object: BCL signatures are not deterministic, so two
    /// signings of one Roster differ in their bytes and hash.
    /// </summary>
    /// <param name="userName">The user: <c>alice</c>, <c>bob</c> or <c>eve</c>.</param>
    /// <returns>The signed Roster.</returns>
    /// <exception cref="ArgumentException">The user is not a published one.</exception>
    public static VerifiedRoster RosterOf(string userName) =>
        FirstRosters.TryGetValue(userName, out VerifiedRoster? roster) ? roster
        : throw new ArgumentException($"No published test user is called '{userName}'; the users are alice, bob and eve.", nameof(userName));

    /// <summary>
    /// A new Roster of a published user listing every published device of that user, signed by the user key. Each call
    /// signs anew, so it is a new object with its own hash; hand the same instance to every node that should hold it.
    /// </summary>
    /// <param name="userName">The user: <c>alice</c>, <c>bob</c> or <c>eve</c>.</param>
    /// <param name="seq">The Roster's sequence number; a higher one replaces a lower (SPEC.md §8.8).</param>
    /// <param name="removed">The devices the Roster marks removed.</param>
    /// <returns>The signed Roster.</returns>
    /// <exception cref="ArgumentException">The user or a removed device is not a published one of that user.</exception>
    public static VerifiedRoster RosterOf(string userName, ulong seq, params string[] removed)
    {
        ArgumentNullException.ThrowIfNull(removed);
        TestIdentity[] devices = [.. All.Where(i => i.UserName == userName)];
        if (devices.Length == 0)
            throw new ArgumentException($"No published test user is called '{userName}'; the users are alice, bob and eve.", nameof(userName));
        string? stranger = removed.FirstOrDefault(r => devices.All(d => d.Name != r));
        if (stranger is not null)
            throw new ArgumentException($"'{stranger}' is not a published device of {userName}.", nameof(removed));

        ulong addedAt = (ulong)Epoch.ToUnixTimeMilliseconds();
        return new RosterDraft
        {
            Seq = seq,
            Devices = [.. devices.Select(d => new RosterDevice(d.Device.PublicKey, d.Name, addedAt, removed.Contains(d.Name)))],
        }.Sign(devices[0].User);
    }

    /// <inheritdoc />
    public override string ToString() => Name;

    private static List<TestIdentity> Build()
    {
        var keys = TestKeys.All;
        var identities = new List<TestIdentity>();
        foreach ((string name, string role, string pkcs8, EcdsaSigner signer) in keys)
        {
            if (role != "device")
                continue;
            string userName = name.Split('-')[0];
            (string _, string _, string userPkcs8, EcdsaSigner user) = keys.First(k => k.Role == "user" && k.Name == userName);
            identities.Add(new TestIdentity(name, userName, user, signer, pkcs8, userPkcs8));
        }

        return identities;
    }
}

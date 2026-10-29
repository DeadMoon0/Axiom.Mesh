using System;
using System.Linq;

namespace Axiom.Mesh.Tests.Support;

/// <summary>Objects made with the producers from the test identities, and the parse helpers the tests share.</summary>
internal static class Samples
{
    /// <summary>A nonce of 32 equal bytes.</summary>
    public static PackageNonce Nonce(byte fill) => new(new Hash32(Enumerable.Repeat(fill, Hash32.Length).ToArray()));

    /// <summary>A Channel owned by <paramref name="owner"/> (Alice by default).</summary>
    public static VerifiedManifest Channel(ISigner? owner = null, ulong seq = 1, byte nonce = 1, string name = "Family") =>
        new ManifestDraft { Nonce = Nonce(nonce), Kind = PackageKind.Channel, Seq = seq, Name = name, Created = 1_767_225_600_000 }
            .Sign(owner ?? TestKeys.Alice);

    /// <summary>A Roster of <paramref name="user"/> listing the given devices.</summary>
    public static VerifiedRoster Roster(ISigner user, ulong seq, params (ISigner Device, bool Removed)[] devices) =>
        new RosterDraft
        {
            Seq = seq,
            Devices = [.. devices.Select((d, i) => new RosterDevice(d.Device.PublicKey, "device-" + i, 1_767_225_600_000, d.Removed))],
        }.Sign(user);

    /// <summary>A Grant of Alice's <paramref name="manifest"/> to <paramref name="grantee"/>.</summary>
    public static VerifiedGrant Grant(VerifiedManifest manifest, ISigner grantee, GrantRights rights, ulong seq = 1) =>
        new GrantDraft { Grantee = grantee.PublicKey, Rights = rights, Seq = seq }.Sign(TestKeys.Alice, manifest);

    /// <summary>A Message Entry written by <paramref name="device"/>.</summary>
    public static VerifiedEntry Message(ISigner device, PackageId package, ulong clock, EntryParents parents, string text = "hello") =>
        new EntryDraft
        {
            Package = package,
            Clock = clock,
            Parents = parents,
            Payload = new MessagePayload { Text = text, Attachments = [], ReplyTo = default },
        }.Sign(device);

    /// <summary>Parses bytes that must be well-formed as a <typeparamref name="T"/>.</summary>
    public static T Parse<T>(ReadOnlyMemory<byte> bytes)
        where T : ParsedObject
    {
        bool parsed = Objects.TryParse(HashedObject.Of(bytes), out ParsedObject? result, out ObjectFault fault);
        Assert.True(parsed, "Expected a well-formed object, got " + fault);
        return Assert.IsAssignableFrom<T>(result);
    }

    /// <summary>The fault of bytes that must be malformed.</summary>
    public static ObjectFault Fault(ReadOnlyMemory<byte> bytes)
    {
        bool parsed = Objects.TryParse(HashedObject.Of(bytes), out ParsedObject? result, out ObjectFault fault);
        Assert.False(parsed, "Expected a malformed object, got a " + result?.GetType().Name);
        return fault;
    }
}

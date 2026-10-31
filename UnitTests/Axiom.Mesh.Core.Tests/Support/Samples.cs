using System;
using System.Linq;
using Axiom.Mesh.Payloads;

namespace Axiom.Mesh.Core.Tests.Support;

/// <summary>Signed and unsigned objects made with Model's producers from the published test identities.</summary>
internal static class Samples
{
    /// <summary>The time every sample says it was made at.</summary>
    public const ulong Created = 1_767_225_600_000;

    /// <summary>A nonce of 32 equal bytes.</summary>
    public static PackageNonce Nonce(byte fill) => new(new Hash32(Enumerable.Repeat(fill, Hash32.Length).ToArray()));

    /// <summary>A Channel owned by <paramref name="owner"/>.</summary>
    public static VerifiedManifest Channel(ISigner owner, ulong seq = 1, byte nonce = 1, Retention retention = Retention.Default) =>
        new ManifestDraft { Nonce = Nonce(nonce), Kind = PackageKind.Channel, Seq = seq, Name = "Family", Created = Created, Retention = retention }.Sign(owner);

    /// <summary>An Archive owned by <paramref name="owner"/>.</summary>
    public static VerifiedManifest Archive(ISigner owner, ulong seq = 1, byte nonce = 2, Retention retention = Retention.Default) =>
        new ManifestDraft { Nonce = Nonce(nonce), Kind = PackageKind.Archive, Seq = seq, Name = "Photos", Created = Created, Retention = retention }.Sign(owner);

    /// <summary>A Roster of <paramref name="user"/> listing the given devices.</summary>
    public static VerifiedRoster Roster(ISigner user, ulong seq, params (ISigner Device, bool Removed)[] devices) =>
        new RosterDraft
        {
            Seq = seq,
            Devices = [.. devices.Select((d, i) => new RosterDevice(d.Device.PublicKey, "device-" + i, Created, d.Removed))],
        }.Sign(user);

    /// <summary>A Grant of <paramref name="manifest"/>'s Package to <paramref name="grantee"/>, signed by the owner.</summary>
    public static VerifiedGrant Grant(ISigner owner, VerifiedManifest manifest, ISigner grantee, GrantRights rights, ulong seq = 1) =>
        new GrantDraft { Grantee = grantee.PublicKey, Rights = rights, Seq = seq }.Sign(owner, manifest);

    /// <summary>A Message written by <paramref name="device"/>.</summary>
    public static VerifiedEntry Message(ISigner device, PackageId package, ulong clock, EntryParents parents, string text = "hello", params Attachment[] attachments) =>
        new EntryDraft
        {
            Package = package,
            Clock = clock,
            Parents = parents,
            Body = new MessagePayload { Text = text, Attachments = attachments, ReplyTo = default }.ToBody(),
        }.Sign(device);

    /// <summary>An Entry with any body, written by <paramref name="device"/>.</summary>
    public static VerifiedEntry Written(ISigner device, PackageId package, ulong clock, EntryParents parents, EntryBody body) =>
        new EntryDraft { Package = package, Clock = clock, Parents = parents, Body = body }.Sign(device);

    /// <summary>A Version naming <paramref name="tree"/>, written by <paramref name="device"/>.</summary>
    public static VerifiedEntry Version(ISigner device, PackageId package, ulong clock, EntryParents parents, ObjectHash tree) =>
        new EntryDraft
        {
            Package = package,
            Clock = clock,
            Parents = parents,
            Body = new VersionPayload { Tree = tree, Label = "main", Prerelease = string.Empty, Notes = string.Empty }.ToBody(),
        }.Sign(device);

    /// <summary>A Redaction of <paramref name="target"/>, written by <paramref name="device"/>.</summary>
    public static VerifiedEntry Redaction(ISigner device, PackageId package, ulong clock, EntryParents parents, ObjectHash target) =>
        new EntryDraft { Package = package, Clock = clock, Parents = parents, Body = new RedactionPayload { Target = target }.ToBody() }.Sign(device);

    /// <summary>An Entry of an extension kind whose payload is <paramref name="length"/> bytes of <paramref name="fill"/>.</summary>
    public static VerifiedEntry Opaque(ISigner device, PackageId package, ulong clock, int length, byte fill) =>
        new EntryDraft
        {
            Package = package,
            Clock = clock,
            Parents = clock == 0 ? EntryParents.None : EntryParents.Of(new ObjectHash(Hash32.Of([fill]))),
            Body = new EntryBody((EntryKind)99, Enumerable.Repeat(fill, length).ToArray(), EntryReferences.None, default),
        }.Sign(device);

    /// <summary>
    /// The bytes of an Entry whose envelope says what is given, references in the given order (ascending or not), signed by
    /// <paramref name="device"/>: what a buggy or hostile writer could send.
    /// </summary>
    public static byte[] RawEntry(ISigner device, PackageId package, ulong clock, EntryParents parents, EntryKind kind, ObjectHash[] references, ObjectHash redacts, byte[] payload)
    {
        var output = new System.Buffers.ArrayBufferWriter<byte>();
        var writer = new Axiom.ProtoStream.Codecs.WireWriter(output);
        writer.UInt8((byte)ObjectTag.Entry);
        writer.UInt8(Objects.Version);
        package.Write(ref writer);
        device.PublicKey.AsDeviceId().Write(ref writer);
        writer.UInt64BigEndian(clock);
        Wire.WriteListCount(ref writer, parents.Count, Limits.MaxEntryParents);
        foreach (ObjectHash parent in parents)
            parent.Write(ref writer);
        writer.UInt16BigEndian((ushort)kind);
        writer.UInt16BigEndian((ushort)references.Length);
        foreach (ObjectHash reference in references)
            reference.Write(ref writer);
        redacts.Write(ref writer);
        Wire.WriteBlob(ref writer, payload, Limits.MaxPayloadLength);
        Signature64 signature = device.Sign(Digests.Of(SigningDomain.Entry, output.WrittenSpan));
        var signing = new Axiom.ProtoStream.Codecs.WireWriter(output);
        signature.Write(ref signing);
        return output.WrittenSpan.ToArray();
    }

    /// <summary>A Chunk of <paramref name="length"/> bytes of <paramref name="fill"/>.</summary>
    public static Chunk Chunk(byte fill, int length = 1000) => ObjectBuilders.Chunk(Enumerable.Repeat(fill, length).ToArray());

    /// <summary>A Tree of one file.</summary>
    public static Tree Tree(string name, ChunkList file)
    {
        Assert.True(ObjectBuilders.TryTree([new TreeEntry(name, TreeEntryMode.File, file.Hash, file.Size)], out Tree? tree, out string? problem), problem);
        return tree;
    }

    /// <summary>A Tree of one subdirectory.</summary>
    public static Tree Folder(string name, Tree child)
    {
        Assert.True(ObjectBuilders.TryTree([new TreeEntry(name, TreeEntryMode.Directory, child.Hash, 0)], out Tree? tree, out string? problem), problem);
        return tree;
    }

    /// <summary>Admits a verified Entry against <paramref name="trust"/>, which must allow it.</summary>
    public static AdmittedEntry Admit(VerifiedEntry entry, ITrustSnapshotView trust) =>
        Assert.IsType<AdmissionDecision.Admit>(EntryAdmission.Decide(entry, trust)).Entry;

    /// <summary>Parses bytes that must be well-formed.</summary>
    public static T Parse<T>(ReadOnlyMemory<byte> bytes)
        where T : ParsedObject
    {
        Assert.True(Objects.TryParse(HashedObject.Of(bytes), out ParsedObject? parsed, out ObjectFault fault), fault.ToString());
        return Assert.IsAssignableFrom<T>(parsed);
    }
}

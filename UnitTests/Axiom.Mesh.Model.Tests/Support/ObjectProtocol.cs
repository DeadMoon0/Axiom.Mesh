using System.Buffers;
using System.Collections.Generic;
using Axiom.Mesh.Payloads;
using Axiom.ProtoStream;
using Axiom.ProtoStream.Framing;

namespace Axiom.Mesh.Tests.Support;

/// <summary>A protocol whose only message is one object, for ProtoStream's round-trip and fuzz harnesses.</summary>
internal static class ObjectProtocol
{
    public static ProtocolDefinition<ObjectFrame, ObjectFrame> Definition { get; } =
        Protocol.Describe<ObjectFrame, ObjectFrame>("axiom-mesh-objects/test")
            .Framing(Framers.LengthPrefixed(LengthPrefix.UInt32BigEndian, Limits.MaxObjectLength + 1))
            .Messages(m => m.Add<ObjectFrame>(1))
            .States(s => s.Start("Open").In("Open").On<ObjectFrame>().Delegate().OnSend<ObjectFrame>())
            .Limits(l => l.MaxBufferedBytes(Limits.MaxFrameLength + 8))
            .Build();

    /// <summary>One sample of every object kind, as the producers make them.</summary>
    public static IReadOnlyList<ParsedObject> Samples()
    {
        VerifiedManifest manifest = Support.Samples.Channel();
        Chunk chunk = ObjectBuilders.Chunk("Hello, Axiom Mesh!\n"u8);
        ChunkList list = ObjectBuilders.ChunkList(19, [chunk.Hash]);
        Assert.True(ObjectBuilders.TryTree([new TreeEntry("hello.txt", TreeEntryMode.File, list.Hash, 19), new TreeEntry("sub", TreeEntryMode.Directory, list.Hash, 0)], out Tree? tree, out _));
        VerifiedEntry version = new EntryDraft
        {
            Package = manifest.Content.Package,
            Clock = 3,
            Parents = EntryParents.Of(Hash(1), Hash(2)),
            Body = new VersionPayload { Tree = tree.Hash, Label = "main", Prerelease = "beta.2", Notes = "ünïcödé notes" }.ToBody(),
        }.Sign(TestKeys.AliceLaptop);
        VerifiedEntry message = new EntryDraft
        {
            Package = manifest.Content.Package,
            Clock = 0,
            Parents = EntryParents.None,
            Body = new MessagePayload { Text = "hi", Attachments = [new Attachment("a.txt", list.Hash, 19)], ReplyTo = Hash(3) }.ToBody(),
        }.Sign(TestKeys.AlicePhone);
        VerifiedEntry redaction = new EntryDraft
        {
            Package = manifest.Content.Package,
            Clock = 4,
            Parents = EntryParents.Of(Hash(4)),
            Body = new RedactionPayload { Target = message.Hash }.ToBody(),
        }.Sign(TestKeys.AlicePhone);

        return
        [
            chunk,
            list,
            tree,
            Support.Samples.Parse<UnverifiedEntry>(version.Bytes),
            Support.Samples.Parse<UnverifiedEntry>(message.Bytes),
            Support.Samples.Parse<UnverifiedEntry>(redaction.Bytes),
            manifest,
            Support.Samples.Roster(TestKeys.Alice, 2, (TestKeys.AliceLaptop, false), (TestKeys.AlicePhone, true)),
            Support.Samples.Grant(manifest, TestKeys.Bob, GrantRights.Write),
            Support.Samples.Parse<OpaqueObject>(RawObjects.Unsigned(42, o => o.Write("extension"u8))),
        ];
    }

    private static ObjectHash Hash(byte fill) => RawObjects.Hash(fill);
}

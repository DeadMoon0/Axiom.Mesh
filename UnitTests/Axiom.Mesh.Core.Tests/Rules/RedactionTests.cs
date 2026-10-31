using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Storage;
using Axiom.Mesh.Core.Tests.Support;
using Axiom.Mesh.Payloads;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>SPEC.md §9.4 and LOCAL-SPEC.md §5.6: the Host sets <c>redacted</c> from the redactions it holds, by their <c>redacts</c>.</summary>
public sealed class RedactionTests
{
    // SPEC.md §9.4: a Redaction applies to a target of the same Package whenever both are stored, whatever their order;
    // it is honoured from the target's own device or a device of the owner, and ignored otherwise, or when its target is
    // itself a Redaction. The redacted Entry keeps its bytes.
    [Fact]
    public async Task AnHonouredRedactionMarksItsTargetRedacted()
    {
        await using TestStore node = await TestStore.StartAsync();
        await node.IdentifyAsync(TestKeys.Alice, TestKeys.AliceLaptop);
        await node.PutAsync(Samples.Roster(TestKeys.Alice, 1, (TestKeys.AliceLaptop, false)));
        VerifiedManifest channel = Samples.Channel(TestKeys.Alice);
        PackageId id = channel.Content.Package;
        await node.FollowAsync(id);
        await node.PutAsync(channel);
        await node.PutAsync(Samples.Grant(TestKeys.Alice, channel, TestKeys.Bob, GrantRights.Write));
        await node.PutAsync(Samples.Roster(TestKeys.Bob, 1, (TestKeys.BobDesktop, false)));
        using ChangeSubscription changes = node.Bus.Subscribe(64);

        VerifiedEntry bobs = Samples.Message(TestKeys.BobDesktop, id, 0, EntryParents.None, "by bob");
        VerifiedEntry byAuthor = Samples.Redaction(TestKeys.BobDesktop, id, 1, EntryParents.Of(bobs.Hash), bobs.Hash);
        VerifiedEntry bobsSecond = Samples.Message(TestKeys.BobDesktop, id, 2, EntryParents.Of(byAuthor.Hash), "again");
        VerifiedEntry byOwner = Samples.Redaction(TestKeys.AliceLaptop, id, 3, EntryParents.Of(bobsSecond.Hash), bobsSecond.Hash);
        VerifiedEntry alices = Samples.Message(TestKeys.AliceLaptop, id, 4, EntryParents.Of(byOwner.Hash), "by alice");
        VerifiedEntry byStranger = Samples.Redaction(TestKeys.BobDesktop, id, 5, EntryParents.Of(alices.Hash), alices.Hash);
        VerifiedEntry ofARedaction = Samples.Redaction(TestKeys.BobDesktop, id, 6, EntryParents.Of(byStranger.Hash), byAuthor.Hash);

        // The redaction by its author arrives before its target.
        foreach (VerifiedEntry entry in new[] { byAuthor, bobs, bobsSecond, byOwner, alices, byStranger, ofARedaction })
            await node.PutFromPeerAsync(Samples.Admit(entry, node.Trust), TestKeys.BobDesktop);

        Dictionary<ObjectHash, bool> redacted = (await node.RowsAsync<EntryRow>()).ToDictionary(r => new ObjectHash(RowKeys.Hash(r.Hash)), r => r.Redacted);
        Assert.True(redacted[bobs.Hash], "A Redaction by the target's own device, stored before it, was not honoured.");
        Assert.True(redacted[bobsSecond.Hash], "A Redaction by an owner device was not honoured.");
        Assert.False(redacted[alices.Hash], "A Redaction by neither the author nor an owner device was honoured.");
        Assert.False(redacted[byAuthor.Hash], "A Redaction of a Redaction was honoured.");
        Assert.True(node.Files.TryOpenRead(bobs.Hash, out System.IO.Stream? kept));
        kept.Dispose();

        var announced = new List<ObjectHash>();
        while (changes.Reader.TryRead(out ChangeSet? set))
        {
            announced.AddRange(set.Redacted);
            announced.AddRange(set.Entries.Where(e => e.Redacted).Select(e => e.Hash));
        }

        Assert.Equal(new[] { bobs.Hash, bobsSecond.Hash }.OrderBy(h => h), announced.Distinct().OrderBy(h => h));
    }

    // SPEC.md §8.6, §9.4 (draft 5): the node reads redactions from the envelope. An Entry of any kind whose redacts names
    // a target withdraws it under the rules of §9.4; an Entry of kind 3 whose payload names a target but whose redacts is
    // zero withdraws nothing, because the node never reads a payload.
    [Fact]
    public async Task ARedactionIsReadFromTheEnvelope_AnyKindWithRedactsWithdraws_APayloadAloneDoesNot()
    {
        await using TestStore node = await TestStore.StartAsync();
        await node.IdentifyAsync(TestKeys.Alice, TestKeys.AliceLaptop);
        await node.PutAsync(Samples.Roster(TestKeys.Alice, 1, (TestKeys.AliceLaptop, false)));
        VerifiedManifest channel = Samples.Channel(TestKeys.Alice);
        PackageId id = channel.Content.Package;
        await node.FollowAsync(id);
        await node.PutAsync(channel);

        VerifiedEntry first = Samples.Message(TestKeys.AliceLaptop, id, 0, EntryParents.None, "first");
        VerifiedEntry second = Samples.Message(TestKeys.AliceLaptop, id, 1, EntryParents.Of(first.Hash), "second");
        var byEnvelope = new EntryBody((EntryKind)500, new byte[] { 1 }, EntryReferences.None, first.Hash);
        var byPayloadOnly = new EntryBody(EntryKind.Redaction, PayloadEncoder.Encode(new RedactionPayload { Target = second.Hash }), EntryReferences.None, default);
        VerifiedEntry withdraws = Samples.Written(TestKeys.AliceLaptop, id, 2, EntryParents.Of(second.Hash), byEnvelope);
        VerifiedEntry namesOnly = Samples.Written(TestKeys.AliceLaptop, id, 3, EntryParents.Of(withdraws.Hash), byPayloadOnly);
        foreach (VerifiedEntry entry in new[] { first, second, withdraws, namesOnly })
            await node.WriteAsync(entry);

        Dictionary<ObjectHash, EntryRow> rows = (await node.RowsAsync<EntryRow>()).ToDictionary(r => new ObjectHash(RowKeys.Hash(r.Hash)));
        Assert.True(rows[first.Hash].Redacted, "An Entry of kind 500 whose redacts names the target did not withdraw it.");
        Assert.False(rows[second.Hash].Redacted, "A payload naming a target withdrew it without redacts.");
        Assert.Equal(RowKeys.Of(first.Hash.Value), rows[withdraws.Hash].Target);
        Assert.Null(rows[namesOnly.Hash].Target);
    }
}

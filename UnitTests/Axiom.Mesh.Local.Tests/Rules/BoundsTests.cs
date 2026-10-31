using System;
using Axiom.Mesh.Local.Tests.Support;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local.Tests.Rules;

/// <summary>
/// Bounds versus substance (LOCAL-SPEC.md §3.2): a value outside its bounds (length, count, width, <c>bool</c>,
/// <c>optional</c>, UTF-8) is a violation in a request and a Host defect in a response or an event; a value in bounds
/// but wrong in substance (an empty name, a relative path, an undefined enumerated value) reads, and the Host answers it
/// <c>Invalid</c>.
/// </summary>
public sealed class BoundsTests
{
    [Fact]
    public void AFieldOutsideItsBoundsInARequestIsAViolation()
    {
        // A bool of 2 (Fetch.recursive).
        Assert.Equal(DecodeStatus.Invalid, AtHost(Fetch.Id, (ref WireWriter w) => { Id(ref w); Id(ref w); w.UInt8(2); w.Boolean(false); }));

        // An optional whose first byte is 2 (ListPackages.after).
        Assert.Equal(DecodeStatus.Invalid, AtHost(ListPackages.Id, (ref WireWriter w) => { w.UInt8(2); }));

        // A string one byte over its 64 (CreateIdentity.userName), and one with invalid UTF-8.
        Assert.Equal(DecodeStatus.Invalid, AtHost(CreateIdentity.Id, (ref WireWriter w) => { Text(ref w, new string('a', 65)); Text(ref w, "laptop"); }));
        Assert.Equal(DecodeStatus.Invalid, AtHost(CreateIdentity.Id, (ref WireWriter w) => { w.UInt16BigEndian(2); w.Bytes([0xC3, 0x28]); Text(ref w, "laptop"); }));

        // A list one over its 1 024 (Unpin.hashes), and a body with a byte left over.
        Assert.Equal(DecodeStatus.Invalid, AtHost(Unpin.Id, (ref WireWriter w) => { w.UInt16BigEndian(1_025); }));
        Assert.Equal(DecodeStatus.Invalid, AtHost(GetIdentity.Id, (ref WireWriter w) => { w.UInt8(0); }));

        // A Hello asking for a role outside the registry (§4.2 check 2).
        byte[] hello = Frames.FromClient(new Hello { Role = Role.User, Name = "app" });
        hello[8] = 3;
        Assert.Equal(DecodeStatus.Invalid, Frames.AtHost(hello).Status);
    }

    [Fact]
    public void AValueInBoundsButWrongInSubstanceReads_TheHostAnswersItInvalid()
    {
        // Empty names, undefined enumerated values: all in bounds.
        Assert.Equal(DecodeStatus.Message, AtHost(CreateIdentity.Id, (ref WireWriter w) => { Text(ref w, ""); Text(ref w, ""); }));
        Assert.Equal(DecodeStatus.Message, AtHost(FollowPackage.Id, (ref WireWriter w) => { Id(ref w); w.UInt8(200); }));
        Assert.Equal(DecodeStatus.Message, AtHost(Subscribe.Id, (ref WireWriter w) => { w.UInt32BigEndian(1); w.UInt16BigEndian(999); new Filter().Write(ref w); }));
        Assert.Equal(DecodeStatus.Message, AtHost(StartPairing.Id, (ref WireWriter w) => { w.UInt8(77); Text(ref w, "not a code"); }));
    }

    [Fact]
    public void AFieldOutsideItsBoundsInAResponseOrAnEventIsAHostDefect()
    {
        // An Identity whose bool is 2: the response reads, its body does not, and the client treats it as a lost connection.
        byte[] identity = Frames.Write((ref WireWriter w) => { w.UInt32BigEndian(1); w.UInt16BigEndian(0); w.UInt8(2); });
        var response = (Response)Frames.AtClient(Frames.Raw(LocalMessageIds.Response, identity)).Message!;
        Assert.False(response.TryReadOk<GetIdentity, Identity>(out _));

        // An event's body is read with the frame: out of bounds is a violation at once.
        byte[] lapse = Frames.Write((ref WireWriter w) => { w.UInt32BigEndian(1); w.UInt32BigEndian(3); });
        Assert.Equal(DecodeStatus.Invalid, Frames.AtClient(Frames.Raw(SubscriptionLapsed.Id, lapse)).Status);

        // A response for correlation 0 is a Host defect too (§3.2: a correlation is never 0).
        Assert.Equal(DecodeStatus.Invalid, Frames.AtClient(Frames.Raw(LocalMessageIds.Response, [0, 0, 0, 0, 0, 0])).Status);
    }

    [Fact]
    public void AWriterRefusesAValueOutsideItsBounds_SoWhatThisSideSendsIsWellFormed()
    {
        Assert.Throws<ArgumentException>(() => Frames.FromClient(new RequestFrame(1, new CreateIdentity { UserName = new string('a', 65), DeviceName = "laptop" })));
        Assert.Throws<ArgumentException>(() => Frames.FromClient(new RequestFrame(1, new Unpin { Hashes = new ObjectHash[1_025] })));
        Assert.Throws<ArgumentException>(() => Frames.FromClient(new Hello { Role = Role.None, Name = "app" }));
        Assert.Throws<ArgumentException>(() => new RequestFrame(0, new GetIdentity()));
    }

    private static DecodeStatus AtHost(ushort id, Frames.WriteAction body) =>
        Frames.AtHost(Frames.Raw(id, Frames.Join(Frames.Write((ref WireWriter w) => w.UInt32BigEndian(1)), Frames.Write(body)))).Status;

    private static void Id(ref WireWriter writer) => Samples.Hash(1).Write(ref writer);

    private static void Text(ref WireWriter writer, string value) => Wire.WriteString(ref writer, value, 4_096);
}

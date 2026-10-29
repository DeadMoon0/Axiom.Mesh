using Axiom.Mesh.Local.Tests.Support;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local.Tests.Rules;

/// <summary>
/// Tolerance of LOCAL-SPEC.md §1.5: an enumerated value a client does not know in a <c>Response</c>, <c>Event</c> or
/// <c>Callback</c> (a change kind, a pin reason, a peer state, an operation kind, a status code) is read and kept,
/// never a decode failure; only a structural bound is a Host defect.
/// </summary>
public sealed class EnumToleranceTests
{
    [Fact]
    public void AnUnknownEnumeratedValueInAnEventIsReadAndKept()
    {
        Package package = Samples.Of<Package>() with { State = (PackageState)9, Policy = (LocalPolicy)200, Kind = (PackageKind)4_000 };
        var changed = new PackageChanged { Change = (PackageChange)200, Package = package };
        var peer = new PeerChanged { Change = (PeerChange)99, Peer = Samples.Of<Peer>() with { State = (PeerState)77, Sources = (PeerSources)0xF0 } };
        var operation = new OperationChanged { Operation = Samples.Of<Operation>() with { Kind = (OperationKind)300, State = (OperationState)8, Status = (Status)999 } };
        var user = new UserChanged { Change = (UserChange)50, User = Samples.Of<User>(), Devices = [] };

        var readChanged = (PackageChanged)Decoded(changed);
        Assert.Equal((PackageChange)200, readChanged.Change);
        Assert.Equal((PackageState)9, readChanged.Package.State);
        Assert.Equal((LocalPolicy)200, readChanged.Package.Policy);
        Assert.Equal((PackageKind)4_000, readChanged.Package.Kind);

        var readPeer = (PeerChanged)Decoded(peer);
        Assert.Equal((PeerChange)99, readPeer.Change);
        Assert.Equal((PeerState)77, readPeer.Peer.State);
        Assert.Equal((PeerSources)0xF0, readPeer.Peer.Sources);

        var readOperation = (OperationChanged)Decoded(operation);
        Assert.Equal((OperationKind)300, readOperation.Operation.Kind);
        Assert.Equal((OperationState)8, readOperation.Operation.State);
        Assert.Equal((Status)999, readOperation.Operation.Status);

        Assert.Equal((UserChange)50, ((UserChanged)Decoded(user)).Change);
    }

    [Fact]
    public void AnUnknownStatusIsAFailureWithItsError_AndAnUnknownPinReasonIsKept()
    {
        var response = (Response)Frames.AtClient(Frames.FromHost(Response.Failure(3, (Status)21, new Error { Message = "newer" }))).Message!;
        Assert.Equal((Status)21, response.Status);
        Assert.True(response.TryReadError(out Error? error));
        Assert.Equal("newer", error.Message);

        var pins = new PinPage { Pins = [Samples.Of<Pin>() with { Reason = (PinReason)6 }] };
        var listed = (Response)Frames.AtClient(Frames.FromHost(Response.Ok<ListPins, PinPage>(4, pins))).Message!;
        Assert.True(listed.TryReadOk<ListPins, PinPage>(out PinPage? page));
        Assert.Equal((PinReason)6, page.Pins[0].Reason);
    }

    [Fact]
    public void AnUnknownByeReasonIsReadAsUnspecified()
    {
        byte[] bye = Frames.Write((ref WireWriter w) => { w.UInt8(42); Wire.WriteString(ref w, "", 256); });

        var read = (Bye)Frames.AtClient(Frames.Raw(LocalMessageIds.Bye, bye)).Message!;

        Assert.Equal(ByeReason.Unspecified, read.Reason);
    }

    private static LocalEvent Decoded(LocalEvent @event)
    {
        DecodeOutcome<IHostMessage> outcome = Frames.AtClient(Frames.FromHost(new EventFrame(1, @event)));
        Assert.Equal(DecodeStatus.Message, outcome.Status);
        return ((EventFrame)outcome.Message!).Event;
    }
}

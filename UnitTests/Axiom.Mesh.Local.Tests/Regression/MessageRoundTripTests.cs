using System.Linq;
using Axiom.Mesh.Local.Tests.Support;
using Axiom.ProtoStream.Testing;

namespace Axiom.Mesh.Local.Tests.Regression;

/// <summary>
/// Every frame of the local protocol, written by one side's definition, reads back on the other side's at every byte
/// split (LOCAL-SPEC.md §3, §15), and re-encodes to the same bytes.
/// </summary>
public sealed class MessageRoundTripTests
{
    // Damage: a field read in another order than written, a forgotten field, or a codec that cannot resume a frame cut
    // anywhere would garble every request a client sends; every request class and the session messages are covered.
    [Theory]
    [MemberData(nameof(MessageSamples.ClientNames), MemberType = typeof(MessageSamples))]
    public void EveryMessageRoundTripsAtEveryByteSplit(string name)
    {
        IClientMessage sent = MessageSamples.FromClient[name];
        CodecHarness.VerifyRoundTrip(
            LocalProtocol.ClientSide,
            LocalProtocol.HostSide,
            [sent],
            (original, read) => Frames.FromClient(original).SequenceEqual(Frames.FromClient(read)));
    }

    // Damage: the same for everything the Host sends: every OK body and record, every event, the callback, and the
    // frames of ids the client does not know.
    [Theory]
    [MemberData(nameof(MessageSamples.HostNames), MemberType = typeof(MessageSamples))]
    public void EveryHostMessageRoundTripsAtEveryByteSplit(string name)
    {
        IHostMessage sent = MessageSamples.FromHost[name];
        CodecHarness.VerifyRoundTrip(
            LocalProtocol.HostSide,
            LocalProtocol.ClientSide,
            [sent],
            (original, read) => Frames.FromHost(original).SequenceEqual(Frames.FromHost(read)));
    }
}

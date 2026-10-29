using System;
using System.Collections.Generic;
using System.Linq;
using Axiom.Mesh.Protocol.Tests.Support;
using Axiom.ProtoStream.Testing;

namespace Axiom.Mesh.Protocol.Tests.Regression;

/// <summary>Every message of both definitions survives encoding and decoding at every byte split (SPEC.md §15).</summary>
public sealed class MessageRoundTripTests
{
    public static TheoryData<int> Samples() => [.. Enumerable.Range(0, MessageSamples.All().Count)];

    // Damage: a field written in another order or width than it is read, a list count off by one, or a reader that
    // assumes a whole frame arrives in one segment breaks every peer, and only at the split where it happens.
    [Theory]
    [MemberData(nameof(Samples))]
    public void EveryMessageRoundTripsAtEveryByteSplit(int sample)
    {
        MeshMessage message = MessageSamples.All()[sample];

        CodecHarness.VerifyRoundTrip(Amp.Definition, [message], MessageSamples.Same);
    }

    // Damage: a codec that keeps state between frames, or reads past its frame, would pass every message alone and fail
    // on a stream of them.
    [Fact]
    public void AStreamOfEveryMessageRoundTrips()
    {
        IReadOnlyList<MeshMessage> all = [.. MessageSamples.All().Where(m => m is not ObjectMessage { Bytes.Length: > 4096 })];

        CodecHarness.VerifyRoundTrip(Amp.Definition, all, MessageSamples.Same);
    }

    // Damage: the pairing messages carry a Roster and an optional user key; a key written but not read, or a Roster cut
    // short, ends every pairing in a violation.
    [Fact]
    public void EveryPairingMessageRoundTripsAtEveryByteSplit()
    {
        PairMessage[] messages =
        [
            PairingSamples.AliceShows,
            PairingSamples.NewDeviceShows,
            PairConfirm.Plain,
            PairConfirm.Enrolling(PairingSamples.BobRoster),
            new PairCancel(PairCancelReason.Expired, string.Empty),
            new PairCancel(PairCancelReason.Refused, new string('r', Limits.MaxByeDetailLength)),
        ];

        foreach (PairMessage message in messages)
            CodecHarness.VerifyRoundTrip(PairProtocol.Definition, [message], SamePairMessage);
        CodecHarness.VerifyRoundTrip(PairProtocol.Definition, messages, SamePairMessage);
    }

    private static bool SamePairMessage(PairMessage expected, PairMessage actual) =>
        expected.GetType() == actual.GetType()
        && Frames.Encode(PairProtocol.Definition, expected).AsSpan().SequenceEqual(Frames.Encode(PairProtocol.Definition, actual));
}

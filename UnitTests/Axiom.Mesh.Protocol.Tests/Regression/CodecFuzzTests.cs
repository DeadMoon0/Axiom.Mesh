using System.Linq;
using Axiom.Mesh.Protocol.Tests.Support;
using Axiom.ProtoStream.Testing;

namespace Axiom.Mesh.Protocol.Tests.Regression;

/// <summary>Mutated frames end in a message, an incomplete frame or a violation, never in an exception or a hang.</summary>
public sealed class CodecFuzzTests
{
    private const int Seed = 42;
    private const int Iterations = 20_000;

    // Damage: a reader that trusts a count before checking the bytes left, indexes past its frame, or throws on a malformed
    // string or key lets any peer crash or stall a session with one frame.
    [Fact]
    public void MutatedAmpFramesNeverThrowOrHang()
    {
        byte[][] samples = [.. MessageSamples.All().Where(m => m is not ObjectMessage { Bytes.Length: > 4096 }).Select(Frames.Encode), Frames.Encode(MessageSamples.Chunk(1024))];

        FuzzReport report = CodecHarness.Fuzz(Amp.Definition, samples, Seed, Iterations);

        Assert.Equal(Iterations, report.Iterations);
        Assert.True(report.Invalid > 0 && report.Messages > 0, $"The fuzz run saw {report.Messages} messages and {report.Invalid} violations.");
    }

    // Damage: the same for the pairing protocol, which a stranger on the LAN may reach while a pairing runs.
    [Fact]
    public void MutatedPairingFramesNeverThrowOrHang()
    {
        PairMessage[] messages = [PairingSamples.AliceShows, PairingSamples.NewDeviceShows, PairConfirm.Enrolling(PairingSamples.BobRoster), new PairCancel(PairCancelReason.Busy, "busy")];
        byte[][] samples = [.. messages.Select(m => Frames.Encode(PairProtocol.Definition, m))];

        FuzzReport report = CodecHarness.Fuzz(PairProtocol.Definition, samples, Seed, Iterations);

        Assert.Equal(Iterations, report.Iterations);
        Assert.True(report.Invalid > 0 && report.Messages > 0, $"The fuzz run saw {report.Messages} messages and {report.Invalid} violations.");
    }
}

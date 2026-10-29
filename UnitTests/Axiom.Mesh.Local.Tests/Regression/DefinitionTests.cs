using System;
using System.Collections.Generic;
using System.Linq;
using Axiom.Mesh.Local.Tests.Support;
using Axiom.ProtoStream.Testing;

namespace Axiom.Mesh.Local.Tests.Regression;

/// <summary>The two ProtoStream definitions of the local protocol: lint-clean, and robust against any mutated frame.</summary>
public sealed class DefinitionTests
{
    private const int Seed = 42;
    private const int Iterations = 20_000;

    // Damage: an opted-out limit or a state that accepts nothing would make a session hang or grow without bound; the
    // linter names both.
    [Fact]
    public void BothDefinitionsHaveNoWarnings()
    {
        DefinitionAssert.NoWarnings(LocalProtocol.HostSide);
        DefinitionAssert.NoWarnings(LocalProtocol.ClientSide);
    }

    // Damage: a reader that throws, loops or over-reads on a hostile frame would let any local process crash or hang the
    // Host's read loop; every outcome must be a message, an incomplete frame or a violation.
    [Fact]
    public void TheHostSideSurvivesMutationsOfEveryClientFrame()
    {
        IReadOnlyList<byte[]> samples = [.. MessageSamples.FromClient.Values.Select(Frames.FromClient)];
        FuzzReport report = CodecHarness.Fuzz(LocalProtocol.HostSide, samples, Seed, Iterations);

        Assert.Equal(Iterations, report.Iterations);
        Assert.True(report.Messages > 0 && report.Invalid > 0, $"The fuzzing reached too little: {report.Messages} messages, {report.Invalid} violations.");
    }

    // Damage: the same on the client, which reads everything a Host (or a squatter on the pipe) sends.
    [Fact]
    public void TheClientSideSurvivesMutationsOfEveryHostFrame()
    {
        IReadOnlyList<byte[]> samples = [.. MessageSamples.FromHost.Values.Select(Frames.FromHost)];
        FuzzReport report = CodecHarness.Fuzz(LocalProtocol.ClientSide, samples, Seed, Iterations);

        Assert.Equal(Iterations, report.Iterations);
        Assert.True(report.Messages > 0 && report.Invalid > 0, $"The fuzzing reached too little: {report.Messages} messages, {report.Invalid} violations.");
    }

    // Damage: a definition whose first-message timeout drifted from 10 seconds, or whose idle backstop could close a
    // client that answers every Ping (60 s + 30 s + one frame's 30 s), would break §2.3 or §4.4.
    [Fact]
    public void BothSidesWaitTenSecondsForTheFirstMessageAndNeverCloseAResponsivePeerForBeingQuiet()
    {
        foreach (var limits in new[] { LocalProtocol.HostSide.Limits, LocalProtocol.ClientSide.Limits })
        {
            Assert.Equal(TimeSpan.FromSeconds(10), limits.FirstMessageTimeout);
            Assert.Equal(TimeSpan.FromSeconds(120), limits.IdleTimeout);
            Assert.True(limits.MaxBufferedBytes >= Limits.MaxFrameLength + 4);
        }

        Assert.Equal(["Connected", "Ready", "Closed"], LocalProtocol.HostSide.States);
        Assert.Equal(["Connected", "Ready", "Closed"], LocalProtocol.ClientSide.States);
    }

    // Damage: a GetIdentity request whose frame differed from LOCAL-SPEC.md Appendix A (length, id, correlation, empty
    // body) would not be understood by any other implementation of the protocol.
    [Fact]
    public void AGetIdentityRequestIsLengthSixIdTwoFiftySixAndItsCorrelation()
    {
        byte[] frame = Frames.FromClient(new RequestFrame(1, new GetIdentity()));

        Assert.Equal(new byte[] { 0, 0, 0, 6, 0x01, 0x00, 0, 0, 0, 1 }, frame);
    }
}

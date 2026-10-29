using System.Buffers;
using Axiom.Mesh.Local.Tests.Support;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local.Tests.Rules;

/// <summary>
/// The correlation rules of LOCAL-SPEC.md §3.4: never 0, never one still outstanding, never a 65th outstanding request;
/// each is a violation (§11.3). A correlation is free again once its response was sent.
/// </summary>
public sealed class CorrelationTests
{
    [Fact]
    public void ACorrelationOf0OrStillOutstanding_OrA65thOutstandingRequest_IsAViolation()
    {
        Assert.Equal(DecodeStatus.Invalid, Frames.AtHost(Request(0)).Status);

        using (var host = HostRunner())
        {
            Assert.Equal(DecodeStatus.Message, Frames.AtHost(host, Request(5)).Status);
            Assert.Equal(DecodeStatus.Invalid, Frames.AtHost(host, Request(5)).Status);
        }

        using (var host = HostRunner())
        {
            for (uint correlation = 1; correlation <= Limits.MaxOutstandingRequests; correlation++)
                Assert.Equal(DecodeStatus.Message, Frames.AtHost(host, Request(correlation)).Status);
            Assert.Equal(DecodeStatus.Invalid, Frames.AtHost(host, Request(Limits.MaxOutstandingRequests + 1)).Status);
        }
    }

    [Fact]
    public void ACorrelationIsFreeAgainOnceItsResponseWasSent_AndAnUnknownRequestCountsLikeAnyOther()
    {
        using var host = HostRunner();
        Assert.Equal(DecodeStatus.Message, Frames.AtHost(host, Request(5)).Status);
        host.Encode(Response.Ok<GetIdentity, Identity>(5, ScriptedHost.Identity), new ArrayBufferWriter<byte>());
        Assert.Equal(DecodeStatus.Message, Frames.AtHost(host, Request(5)).Status);

        byte[] unknown = Frames.FromClient(new UnknownRequest { Id = 900, Correlation = 6 });
        Assert.Equal(DecodeStatus.Message, Frames.AtHost(host, unknown).Status);
        Assert.Equal(DecodeStatus.Invalid, Frames.AtHost(host, unknown).Status);
        host.Encode(Response.ForUnknownRequest(6), new ArrayBufferWriter<byte>());
        Assert.Equal(DecodeStatus.Message, Frames.AtHost(host, unknown).Status);
    }

    private static CodecRunner<IClientMessage, IHostMessage> HostRunner() => new(LocalProtocol.HostSide);

    // A GetIdentity request with any correlation, 0 included, which no writer of this package would produce.
    private static byte[] Request(uint correlation) =>
        Frames.Raw(GetIdentity.Id, [(byte)(correlation >> 24), (byte)(correlation >> 16), (byte)(correlation >> 8), (byte)correlation]);
}

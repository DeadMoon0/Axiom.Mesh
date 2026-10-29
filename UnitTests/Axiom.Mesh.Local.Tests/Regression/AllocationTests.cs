using System;
using System.Buffers;
using Axiom.Mesh.Local.Tests.Support;
using Axiom.Mesh.Tests.Shared;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local.Tests.Regression;

/// <summary>The allocation budget of the client's hot path (plan §3.5): decoding allocates the message, nothing else.</summary>
public sealed class AllocationTests
{
    // Measured 280 bytes on net8.0 and net10.0, x64 Release: the request frame on the way out; the response, the Identity
    // record and its two strings on the way in. The budget leaves room for object headers on other platforms, not for a
    // copy of the frame.
    private const long GetIdentityRoundTripBudget = 320;

    // Damage: a codec that copied a frame into a fresh array, boxed the correlation, built a dictionary per response or
    // parsed through an intermediate buffer would allocate on every request every client sends.
    [ReleaseFact]
    public void AGetIdentityRoundTripOnTheClientAllocatesOnlyTheMessages()
    {
        byte[] response = Frames.FromHost(Response.Ok<GetIdentity, Identity>(1, ScriptedHost.Identity));
        var request = new GetIdentity();
        var output = new ArrayBufferWriter<byte>(64);
        using var runner = new CodecRunner<IHostMessage, IClientMessage>(LocalProtocol.ClientSide);
        var input = new ReadOnlySequence<byte>(response);

        long RoundTrip()
        {
            output.ResetWrittenCount();
            long before = GC.GetAllocatedBytesForCurrentThread();
            runner.Encode(new RequestFrame(1, request), output);
            var decoded = (Response)runner.Decode(input, isCompleted: true).Message!;
            bool read = decoded.TryReadOk<GetIdentity, Identity>(out Identity? identity);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(read && identity!.Present);
            return allocated;
        }

        RoundTrip();
        long allocated = RoundTrip();

        Assert.InRange(allocated, 1, GetIdentityRoundTripBudget);
    }
}

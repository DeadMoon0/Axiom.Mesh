using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local.Tests.Support;
using Axiom.ProtoStream;

namespace Axiom.Mesh.Local.Tests.Rules;

/// <summary>
/// Extensions within version 1 (LOCAL-SPEC.md §1.5): an unknown request is answered <c>UnknownRequest</c> and the
/// connection stays open; an unknown event is read and discarded; an unknown callback is answered
/// <c>Reply(UnknownRequest)</c>. None is a violation.
/// </summary>
public sealed class UnknownIdTests
{
    [Fact]
    public async Task AnUnknownRequestIdIsAnsweredUnknownRequestAndTheConnectionStaysOpen()
    {
        await using SessionPair pair = await SessionPair.OpenAsync(Role.User);

        await pair.Client.WriteAsync(new UnknownRequest { Id = 900, Correlation = 1, Body = new byte[] { 7, 7 } }, CancellationToken.None);
        await pair.Client.WriteAsync(new RequestFrame(2, new GetIdentity()), CancellationToken.None);

        // The Host's definition answered the unknown request itself; its application sees only the known one.
        var known = Assert.IsType<RequestFrame>(await pair.HostReadsAsync());
        Assert.Equal(2U, known.Correlation);
        var unknown = Assert.IsType<Response>(await pair.ClientReadsAsync());
        Assert.Equal(1U, unknown.Correlation);
        Assert.Equal(Status.UnknownRequest, unknown.Status);

        await pair.Host.WriteAsync(Response.Ok<GetIdentity, Identity>(2, ScriptedHost.Identity), CancellationToken.None);
        var answered = Assert.IsType<Response>(await pair.ClientReadsAsync());
        Assert.True(answered.TryReadOk<GetIdentity, Identity>(out Identity? identity));
        Assert.Equal(ScriptedHost.Identity, identity);
        Assert.Equal(SessionStatus.Open, pair.Host.Status);
    }

    [Fact]
    public async Task AnUnknownEventIsDiscardedByTheClient_TheNextOneArrives()
    {
        await using SessionPair pair = await SessionPair.OpenAsync(Role.User);

        await pair.HostWritesRawAsync(Frames.FromHost(new UnknownEvent { Id = 1_200, SubscriptionId = 1, Body = new byte[] { 1 } }));
        await pair.Host.WriteAsync(new EventFrame(1, new StoreChanged { Store = Samples.Of<Store>() }), CancellationToken.None);

        var next = Assert.IsType<EventFrame>(await pair.ClientReadsAsync());
        Assert.IsType<StoreChanged>(next.Event);
        Assert.Equal(SessionStatus.Open, pair.Client.Status);
    }

    [Fact]
    public async Task AnUnknownCallbackIsAnsweredUnknownRequestByTheService()
    {
        await using SessionPair pair = await SessionPair.OpenAsync(Role.Service);

        // The service's definition answers while its application reads; that read returns the next delegated message.
        Task<IHostMessage?> serviceReads = pair.ClientReadsAsync();
        await pair.HostWritesRawAsync(Frames.FromHost(new UnknownCallback { Id = 1_600, CallbackId = 12 }));

        var reply = Assert.IsType<Reply>(await pair.HostReadsAsync());
        Assert.Equal(12U, reply.CallbackId);
        Assert.Equal(Status.UnknownRequest, reply.Status);

        await pair.Host.WriteAsync(new Drain(), CancellationToken.None);
        Assert.IsType<Drain>(await serviceReads);
    }

    [Fact]
    public async Task APingIsAnsweredByEitherDefinitionWithItsNonce()
    {
        await using SessionPair pair = await SessionPair.OpenAsync(Role.User);

        // Each side answers the other's Ping while it reads, so both read at once.
        Task<IClientMessage?> hostReads = pair.HostReadsAsync();
        Task<IHostMessage?> clientReads = pair.ClientReadsAsync();
        await pair.Client.WriteAsync(new Ping { Nonce = 5 }, CancellationToken.None);
        await pair.Host.WriteAsync(new Ping { Nonce = 6 }, CancellationToken.None);

        Assert.Equal(6UL, Assert.IsType<Pong>(await hostReads).Nonce);
        Assert.Equal(5UL, Assert.IsType<Pong>(await clientReads).Nonce);
    }
}

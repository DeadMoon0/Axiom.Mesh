using System;
using System.Threading.Tasks;
using Axiom.Mesh.Integration.Tests.Support;
using Xunit.Abstractions;

namespace Axiom.Mesh.Integration.Tests.Regression;

/// <summary>
/// The chat service's release floors (plan §9.3 item 6) on real nodes over loopback TLS and the real pipe: a 1 MiB attachment
/// end to end within 10 s, a catch-up of 10 000 messages within 10 s. Release only, with <c>AXIOM_MESH_FLOORS=1</c>
/// (<see cref="FloorFactAttribute"/>); each prints what it measured.
/// </summary>
public sealed class ChatFloorTests(ITestOutputHelper output)
{
    // Damage: an attach that read a file twice, a get that fetched Chunk by Chunk without the recursive Fetch, or a job that
    // waited on time instead of events would push one photo past ten seconds.
    [FloorFact]
    [Trait("Category", "Floor")]
    public async Task AOneMebibyteAttachmentGoesFromChatSendToChatGetWithinTenSeconds()
    {
        TimeSpan elapsed = await ChatMeasure.MeasureSendAsync();
        output.WriteLine($"chat-send: {elapsed.TotalSeconds:0.00} s");
        Assert.True(elapsed < TimeSpan.FromSeconds(10), $"1 MiB took {elapsed.TotalSeconds:0.0} s from chat send to chat get.");
    }

    // Damage: a catch-up that rebuilt from the history on every start, committed the cursor per Entry outside the runtime's
    // order, or emitted an event for every old message would push 10 000 messages past ten seconds.
    [FloorFact]
    [Trait("Category", "Floor")]
    public async Task TenThousandMessagesAreCaughtUpWithinTenSeconds()
    {
        TimeSpan elapsed = await ChatMeasure.MeasureCatchUpAsync(10_000);
        output.WriteLine($"chat-catchup: {elapsed.TotalSeconds:0.00} s, {10_000 / elapsed.TotalSeconds:0} messages/s");
        Assert.True(elapsed < TimeSpan.FromSeconds(10), $"10 000 messages took {elapsed.TotalSeconds:0.0} s to catch up.");
    }
}

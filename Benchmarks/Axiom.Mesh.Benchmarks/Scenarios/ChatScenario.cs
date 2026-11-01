using System.IO;
using System.Threading.Tasks;
using Axiom.Mesh.Integration.Tests.Support;

namespace Axiom.Mesh.Benchmarks.Scenarios;

/// <summary>
/// The chat service on two real nodes over loopback TLS and the real pipe (plan §9.3 item 6), measured by the integration
/// suite's <see cref="ChatMeasure"/>: <c>chat-send</c> (a 1 MiB attachment end to end, floor 10 s) and <c>chat-catchup</c>
/// (10 000 messages caught up by a restarted service, floor 10 s).
/// </summary>
internal static class ChatScenario
{
    private const int Messages = 10_000;

    /// <summary>A 1 MiB attachment end to end.</summary>
    /// <param name="output">Where results go.</param>
    /// <returns>A task.</returns>
    public static async Task SendAsync(TextWriter output)
    {
        System.TimeSpan elapsed = await ChatMeasure.MeasureSendAsync().ConfigureAwait(false);
        output.WriteLine($"1 MiB attachment, chat send on Alice to the file written by chat get on Bob: {elapsed.TotalSeconds:0.00} s (floor 10 s)");
    }

    /// <summary>A catch-up of 10 000 messages.</summary>
    /// <param name="output">Where results go.</param>
    /// <returns>A task.</returns>
    public static async Task CatchUpAsync(TextWriter output)
    {
        System.TimeSpan elapsed = await ChatMeasure.MeasureCatchUpAsync(Messages).ConfigureAwait(false);
        output.WriteLine($"{Messages} messages caught up by a restarted chat service: {elapsed.TotalSeconds:0.00} s (floor 10 s), {Messages / elapsed.TotalSeconds:0} messages/s");
    }
}

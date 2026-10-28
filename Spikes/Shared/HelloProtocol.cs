using System.Threading;
using System.Threading.Tasks;
using Axiom.LWSE.Connections;
using Axiom.LWSE.Protocols;
using Axiom.ProtoStream;
using Axiom.ProtoStream.Codecs;
using Axiom.ProtoStream.Framing;

namespace Axiom.Mesh.Spikes;

public abstract class SpikeMessage;

/// <summary>The one message of the spike protocol: each side names itself.</summary>
public sealed class Hello : SpikeMessage, IWireMessage<Hello>
{
    private const int MaxBytes = 256;

    public required string Name { get; init; }

    public static bool TryRead(ref WireReader reader, out Hello message)
    {
        message = new Hello { Name = reader.String(LengthPrefix.UInt16BigEndian, maxBytes: MaxBytes) };
        return reader.Ok;
    }

    public void Write(ref WireWriter writer) => writer.String(LengthPrefix.UInt16BigEndian, Name, maxBytes: MaxBytes);
}

/// <summary>A symmetric protocol: both sides say Hello, then keep the session open until the server retires it.</summary>
public static class HelloProtocol
{
    public const string Alpn = "mesh-spike/1";

    public static ProtocolDefinition<SpikeMessage, SpikeMessage> Definition { get; } = Protocol.Describe<SpikeMessage, SpikeMessage>(Alpn)
        .Framing(Framers.LengthPrefixed(LengthPrefix.UInt32BigEndian, maxFrameSize: 64 * 1024))
        .Messages(m => m.Add<Hello>(1))
        .States(s => s
            .Start("Open")
            .In("Open")
                .On<Hello>().Delegate()
                .OnSend<Hello>())
        .Limits(l => l.NoIdleTimeout())
        .Build();
}

/// <summary>Says hello, logs who answered and over which TLS, and then waits for the server to end the session.</summary>
public sealed class HelloHandler(string node, SpikeLog log) : ISessionHandler<SpikeMessage, SpikeMessage>
{
    public ProtocolDefinition<SpikeMessage, SpikeMessage> Definition => HelloProtocol.Definition;

    public async ValueTask ServeAsync(ConnectionContext connection, Session<SpikeMessage, SpikeMessage> session, CancellationToken stopping)
    {
        await session.WriteAsync(new Hello { Name = node }, stopping).ConfigureAwait(false);
        await foreach (SpikeMessage message in session.Messages.WithCancellation(stopping).ConfigureAwait(false))
        {
            if (message is not Hello hello)
                continue;
            string tls = connection.TryGetFeature(out PeerIdentity? peer) && peer is not null ? peer.ToString() : "no TLS feature";
            log.Add($"[{node}] hello from {hello.Name} on #{connection.Id} via {tls}");
        }
    }
}

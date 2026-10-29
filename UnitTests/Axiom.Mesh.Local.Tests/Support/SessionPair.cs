using System;
using System.Threading;
using System.Threading.Tasks;
using Axiom.ProtoStream;
using Axiom.ProtoStream.Testing;

namespace Axiom.Mesh.Local.Tests.Support;

/// <summary>Both definitions' sessions over one in-memory pipe pair, past <c>Hello</c>/<c>HelloAck</c>, for frame-level tests.</summary>
internal sealed class SessionPair : IAsyncDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly TransportPair transport;
    private readonly Connection clientConnection;
    private readonly Connection hostConnection;

    private SessionPair(TransportPair transport, Connection clientConnection, Session<IHostMessage, IClientMessage> client, Connection hostConnection, Session<IClientMessage, IHostMessage> host)
    {
        this.transport = transport;
        this.clientConnection = clientConnection;
        this.hostConnection = hostConnection;
        Client = client;
        Host = host;
    }

    /// <summary>The client's session, Ready.</summary>
    public Session<IHostMessage, IClientMessage> Client { get; }

    /// <summary>The Host's session, Ready.</summary>
    public Session<IClientMessage, IHostMessage> Host { get; }

    /// <summary>Opens both sessions and exchanges <c>Hello</c> and <c>HelloAck</c> for <paramref name="role"/>.</summary>
    public static async Task<SessionPair> OpenAsync(Role role)
    {
        TransportPair pair = InMemoryTransport.CreatePair();
        Connection clientConnection = Connection.FromPipe(pair.Client);
        Connection hostConnection = Connection.FromPipe(pair.Server);
        Session<IHostMessage, IClientMessage> client = await clientConnection.OpenAsync(LocalProtocol.ClientSide, CancellationToken.None);
        Session<IClientMessage, IHostMessage> host = await hostConnection.OpenAsync(LocalProtocol.HostSide, CancellationToken.None);

        await client.WriteAsync(new Hello { Role = role, Name = "test" }, CancellationToken.None);
        Assert.IsType<Hello>((await host.ReadAsync(CancellationToken.None)).Message);
        await host.WriteAsync(ScriptedHost.Ack(role), CancellationToken.None);
        Assert.IsType<HelloAck>((await client.ReadAsync(CancellationToken.None)).Message);
        return new SessionPair(pair, clientConnection, client, hostConnection, host);
    }

    /// <summary>
    /// Writes a frame onto the Host's end of the pipe past its definition, as a Host of a later version would send a
    /// frame this one does not know. Only while the Host's session writes nothing itself.
    /// </summary>
    public async Task HostWritesRawAsync(byte[] frame)
    {
        await transport.Server.Output.WriteAsync(frame);
        await transport.Server.Output.FlushAsync();
    }

    /// <summary>The next message the Host's session hands its application.</summary>
    public async Task<IClientMessage?> HostReadsAsync() =>
        (await Host.ReadAsync(CancellationToken.None).AsTask().WaitAsync(Patience)).Message;

    /// <summary>The next message the client's session hands its application.</summary>
    public async Task<IHostMessage?> ClientReadsAsync() =>
        (await Client.ReadAsync(CancellationToken.None).AsTask().WaitAsync(Patience)).Message;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await clientConnection.DisposeAsync();
        await hostConnection.DisposeAsync();
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using Axiom.ProtoStream;
using Axiom.ProtoStream.Testing;

namespace Axiom.Mesh.Local.Tests.Support;

/// <summary>
/// A tiny Host for client tests: the Host side of the local protocol over an in-memory pipe pair, driven step by step
/// by the test. It answers <c>Hello</c> with the <see cref="HelloAck"/> it was given and otherwise does only what the
/// test tells it.
/// </summary>
internal sealed class ScriptedHost : IAsyncDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly Connection connection;

    private ScriptedHost(Connection connection, Session<IClientMessage, IHostMessage> session, Hello hello)
    {
        this.connection = connection;
        Session = session;
        Hello = hello;
    }

    /// <summary>The Host's session.</summary>
    public Session<IClientMessage, IHostMessage> Session { get; }

    /// <summary>The client's <c>Hello</c>.</summary>
    public Hello Hello { get; }

    /// <summary>The identity every scripted Host reports.</summary>
    public static Identity Identity { get; } = new()
    {
        Present = true,
        UserId = new UserId(Samples.Hash(1)),
        UserName = "alice",
        UserKeyPresent = true,
        DeviceId = new DeviceId(Samples.Hash(2)),
        DeviceName = "laptop",
        RosterSeq = 1,
        Created = 1_700_000_000_000,
    };

    /// <summary>Connects a client to a new scripted Host.</summary>
    public static async Task<(LocalClient Client, ScriptedHost Host)> ConnectAsync(LocalClientOptions options, HelloAck ack)
    {
        TransportPair pair = InMemoryTransport.CreatePair();
        Task<ScriptedHost> host = AcceptAsync(pair, ack);
        LocalClient client = await LocalClient.OpenAsync(pair.Client, options, CancellationToken.None).WaitAsync(Patience);
        return (client, await host.WaitAsync(Patience));
    }

    /// <summary>Connects a user client named <c>test</c> to a new scripted Host.</summary>
    public static Task<(LocalClient Client, ScriptedHost Host)> ConnectAsync() =>
        ConnectAsync(new LocalClientOptions { Name = "test" }, Ack(Role.User));

    /// <summary>The <c>HelloAck</c> a scripted Host sends.</summary>
    public static HelloAck Ack(Role role) => new()
    {
        HostVersion = "scripted",
        Role = role,
        Capabilities = HostCapabilities.StoreReadable | HostCapabilities.Telemetry,
        Identity = Identity,
        StoreEpoch = Samples.Hash(9),
        StoreSeq = 4_120,
    };

    /// <summary>Reads the next message, which must be a <typeparamref name="T"/>.</summary>
    public async Task<T> ReadAsync<T>()
        where T : class, IClientMessage
    {
        ProtocolReadResult<IClientMessage> read = await Session.ReadAsync(CancellationToken.None).AsTask().WaitAsync(Patience);
        return Assert.IsType<T>(read.Message);
    }

    /// <summary>Reads the next request, which must be a <typeparamref name="TRequest"/>.</summary>
    public async Task<(uint Correlation, TRequest Request)> ReadRequestAsync<TRequest>()
        where TRequest : LocalRequest
    {
        RequestFrame frame = await ReadAsync<RequestFrame>();
        return (frame.Correlation, Assert.IsType<TRequest>(frame.Request));
    }

    /// <summary>Sends a message to the client.</summary>
    public Task SendAsync(IHostMessage message) => Session.WriteAsync(message, CancellationToken.None).AsTask().WaitAsync(Patience);

    /// <summary>Whether the client ended the session: its <c>Bye</c> was read, or its end of the pipe closed.</summary>
    public async Task<bool> EndedAsync()
    {
        ProtocolReadResult<IClientMessage> read = await Session.ReadAsync(CancellationToken.None).AsTask().WaitAsync(Patience);
        return read.IsCompleted || read.Message is Bye;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => connection.DisposeAsync();

    private static async Task<ScriptedHost> AcceptAsync(TransportPair pair, HelloAck ack)
    {
        Connection connection = Connection.FromPipe(pair.Server);
        Session<IClientMessage, IHostMessage> session = await connection.OpenAsync(LocalProtocol.HostSide, CancellationToken.None);
        ProtocolReadResult<IClientMessage> first = await session.ReadAsync(CancellationToken.None);
        Hello hello = Assert.IsType<Hello>(first.Message);
        await session.WriteAsync(ack, CancellationToken.None);
        return new ScriptedHost(connection, session, hello);
    }
}

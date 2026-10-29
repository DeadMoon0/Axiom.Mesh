using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;
using Axiom.ProtoStream;
using Axiom.ProtoStream.Errors;

namespace Axiom.Mesh.Core.Tests.Support;

/// <summary>
/// A local client driven frame by frame, for the local port's tests: the client side of the protocol over a node's
/// in-memory pair, on the mesh's clock, which reads only when the test says so (so a <c>Ping</c> stays unanswered) and
/// can write bytes past the definition (a frame cut short, a correlation of 0).
/// </summary>
internal sealed class RawLocalClient : IAsyncDisposable
{
    private readonly Stream stream;
    private readonly Connection connection;
    private uint lastCorrelation;

    private RawLocalClient(Stream stream, Connection connection, Session<IHostMessage, IClientMessage> session)
    {
        this.stream = stream;
        this.connection = connection;
        Session = session;
    }

    /// <summary>The client's session.</summary>
    public Session<IHostMessage, IClientMessage> Session { get; }

    /// <summary>Connects to a node's local port from a process, without saying anything yet.</summary>
    public static async Task<RawLocalClient> ConnectAsync(TestMesh mesh, string node, TestProcess process)
    {
        Stream stream = mesh[node].ConnectRaw(process);
        Connection connection = Connection.FromStream(stream, new ConnectionOptions { TimeProvider = mesh.Clock });
        Session<IHostMessage, IClientMessage> session = await connection.OpenAsync(LocalProtocol.ClientSide, CancellationToken.None);
        return new RawLocalClient(stream, connection, session);
    }

    /// <summary>Connects and says <c>Hello</c> as a user client of the served user; the Host's answer is returned.</summary>
    public static async Task<(RawLocalClient Client, IHostMessage? Answer)> HelloAsync(TestMesh mesh, string node, Hello hello, TestProcess process)
    {
        RawLocalClient client = await ConnectAsync(mesh, node, process);
        await client.SendAsync(hello);
        return (client, await client.ReadAsync());
    }

    /// <summary>A Ready user client of the served user.</summary>
    public static async Task<RawLocalClient> ReadyAsync(TestMesh mesh, string node)
    {
        (RawLocalClient client, IHostMessage? answer) = await HelloAsync(mesh, node, new Hello { Role = Role.User, Name = "raw" }, TestProcess.Served);
        Assert.IsType<HelloAck>(answer);
        return client;
    }

    /// <summary>Sends a message through the client's definition.</summary>
    public Task SendAsync(IClientMessage message) => Session.WriteAsync(message, CancellationToken.None).AsTask().WaitAsync(TestMesh.Deadline);

    /// <summary>Sends a request with the next correlation.</summary>
    public async Task<uint> RequestAsync(LocalRequest request)
    {
        uint correlation = Interlocked.Increment(ref lastCorrelation);
        await SendAsync(new RequestFrame(correlation, request));
        return correlation;
    }

    /// <summary>Writes bytes onto the pipe past the definition; only while the session writes nothing itself.</summary>
    public async Task WriteRawAsync(byte[] bytes)
    {
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
    }

    /// <summary>A request frame with any correlation, 0 included, written past the definition (LOCAL-SPEC.md §3.4).</summary>
    public static byte[] RequestBytes(ushort id, uint correlation)
    {
        byte[] frame = new byte[4 + 2 + 4];
        BinaryPrimitives.WriteUInt32BigEndian(frame, 6);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4), id);
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(6), correlation);
        return frame;
    }

    /// <summary>The next message the Host sent; <see langword="null"/> when the connection ended.</summary>
    public async Task<IHostMessage?> ReadAsync()
    {
        try
        {
            return (await Session.ReadAsync(CancellationToken.None).AsTask().WaitAsync(TestMesh.Deadline)).Message;
        }
        catch (Exception ex) when (ex is IOException or ProtoStreamException or ObjectDisposedException)
        {
            return null;
        }
    }

    /// <summary>Reads until the response of a correlation; events on the way are skipped.</summary>
    public async Task<Response> ResponseAsync(uint correlation)
    {
        while (true)
        {
            IHostMessage? message = await ReadAsync();
            if (message is null)
                throw new InvalidOperationException($"The connection ended before the response to #{correlation}.");
            if (message is Response response && response.Correlation == correlation)
                return response;
        }
    }

    /// <summary>Whether the Host ended the connection: its Bye, or the end of the pipe.</summary>
    public async Task<Bye?> EndAsync()
    {
        while (true)
        {
            IHostMessage? message = await ReadAsync();
            if (message is null)
                return null;
            if (message is Bye bye)
                return bye;
        }
    }

    /// <summary>Reads raw bytes until the pipe ends; how many arrived. For a Host that closes without a frame.</summary>
    public async Task<int> RawBytesUntilEndAsync()
    {
        byte[] buffer = new byte[256];
        int total = 0;
        while (true)
        {
            int read = await stream.ReadAsync(buffer).AsTask().WaitAsync(TestMesh.Deadline);
            if (read == 0)
                return total;
            total += read;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await connection.DisposeAsync();
        await stream.DisposeAsync();
    }
}

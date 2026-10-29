using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>
/// LOCAL-SPEC.md §2.3 and §4.2: the client sends <c>Hello</c> within 10 seconds, and the Host checks it in the order of
/// §4.2 (version; role; a listed service name, proven by its provenance; one connection per name; the limits of §12) and
/// answers <c>HelloAck</c> or the <c>Bye</c> of the first check that failed.
/// </summary>
public sealed class LocalHelloTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Theory]
    [InlineData("version 2", ByeReason.Refused, "version")] // check 1: only version 1 exists
    [InlineData("role 3", ByeReason.Violation, "")] // check 2: a role outside the registry is a violation (§11.3)
    [InlineData("unlisted name", ByeReason.Refused, "service")] // check 3: the configuration does not list the name
    [InlineData("in-process name on the pipe", ByeReason.Refused, "service")] // check 3: an in-process name only on its own pair
    [InlineData("wrong external token", ByeReason.Refused, "service")] // check 3: an external name needs the configured token
    [InlineData("wrong spawned token", ByeReason.Refused, "service")] // check 3: a spawned name needs the token its child was given
    [InlineData("duplicate", ByeReason.Duplicate, "")] // check 4: a second connection for a live name; the live one is kept
    [InlineData("65th user", ByeReason.Refused, "busy")] // check 5: 64 user connections at once (§12)
    public async Task Refusals(string reason, ByeReason expected, string detail)
    {
        await using TestMesh mesh = await LocalMeshes.StartAsync();
        TestNode node = mesh[LocalMeshes.Node];
        var live = new List<LocalClient>();
        if (reason == "duplicate")
            live.Add(await node.ConnectServiceAsync("cli", LocalMeshes.CliToken, None));
        if (reason == "65th user")
        {
            for (int i = 0; i < Limits.MaxUserConnections; i++)
                live.Add(await node.ConnectUserAsync("user-" + i, None));
        }

        Bye bye = reason == "role 3"
            ? await RefusedRawAsync(mesh, HelloBytes(3, "cli"))
            : await RefusedAsync(mesh, reason switch
            {
                "version 2" => new Hello { Version = 2, Role = Role.User, Name = "app" },
                "unlisted name" => Service("nobody", ServiceToken.None),
                "in-process name on the pipe" => Service("sync", ServiceToken.None),
                "wrong external token" => Service("cli", LocalMeshes.WrongToken),
                "wrong spawned token" => Service("worker", LocalMeshes.WrongToken),
                "duplicate" => Service("cli", LocalMeshes.CliToken),
                _ => new Hello { Role = Role.User, Name = "user-65" },
            });

        Assert.Equal(expected, bye.Reason);
        if (expected != ByeReason.Violation)
            Assert.Equal(detail, bye.Detail);
        foreach (LocalClient kept in live)
            Assert.True((await kept.SendAsync(new GetIdentity(), None)).IsOk, "a live connection is kept");
    }

    // §4.2 check 3 and §13: the in-process service gets its name on its own in-memory pair and nowhere else; a spawned or
    // external program gets its name with exactly its token.
    [Fact]
    public async Task AnInProcessNameIsNeverGrantedOnThePipe_ASpawnedOrExternalNameNeedsItsToken()
    {
        await using TestMesh mesh = await LocalMeshes.StartAsync();
        TestNode node = mesh[LocalMeshes.Node];

        LocalClient sync = await node.ConnectInProcessAsync("sync", None);
        Assert.Equal(Role.Service, sync.Host.Role);
        await sync.ByeAsync(None);
        Assert.Equal("service", (await RefusedAsync(mesh, Service("sync", ServiceToken.None))).Detail);

        LocalClient cli = await node.ConnectServiceAsync("cli", LocalMeshes.CliToken, None);
        LocalClient worker = await node.ConnectServiceAsync("worker", LocalMeshes.WorkerToken, None);
        Assert.Equal(Role.Service, cli.Host.Role);
        Assert.Equal(Role.Service, worker.Host.Role);
        await cli.ByeAsync(None);
        await worker.ByeAsync(None);
        Assert.Equal("service", (await RefusedAsync(mesh, Service("cli", ServiceToken.None))).Detail);
        Assert.Equal("service", (await RefusedAsync(mesh, Service("worker", LocalMeshes.CliToken))).Detail);
    }

    // §2.3: no Hello within 10 seconds closes the connection without a frame; at most 16 wait for Hello, and a 17th closes
    // the one that waited longest.
    [Fact]
    public async Task NoHelloWithin10SecondsClosesWithoutAFrame_A17thWaiterClosesTheLongestWaiting()
    {
        await using TestMesh mesh = await LocalMeshes.StartAsync();
        TestNode node = mesh[LocalMeshes.Node];
        LocalPort port = node.Node.Local;

        await using (Stream silent = node.ConnectRaw(TestProcess.Served))
        {
            await Waits.UntilAsync(() => port.Book.Waiting == 1, "the connection waiting for Hello");
            mesh.Clock.Advance(TimeSpan.FromSeconds(Limits.LocalHelloSeconds));
            Assert.Equal(0, await BytesUntilEndAsync(silent));
        }

        await Waits.UntilAsync(() => port.Book.Waiting == 0, "the silent connection gone");
        var waiting = new List<Stream>();
        for (int i = 1; i <= Limits.MaxConnectionsWaitingForHello; i++)
        {
            waiting.Add(node.ConnectRaw(TestProcess.Served));
            int expected = i;
            await Waits.UntilAsync(() => port.Book.Waiting == expected, $"{expected} connections waiting for Hello");
        }

        await using Stream seventeenth = node.ConnectRaw(TestProcess.Served);
        Assert.Equal(0, await BytesUntilEndAsync(waiting[0]));
        await Waits.UntilAsync(() => port.Book.Waiting == Limits.MaxConnectionsWaitingForHello, "16 waiting again");
        foreach (Stream stream in waiting)
            await stream.DisposeAsync();
    }

    private static Hello Service(string name, ServiceToken token) => new() { Role = Role.Service, Name = name, Token = token };

    private static async Task<Bye> RefusedAsync(TestMesh mesh, Hello hello)
    {
        (RawLocalClient client, IHostMessage? answer) = await RawLocalClient.HelloAsync(mesh, LocalMeshes.Node, hello, TestProcess.Served);
        await using (client)
            return Assert.IsType<Bye>(answer);
    }

    private static async Task<Bye> RefusedRawAsync(TestMesh mesh, byte[] hello)
    {
        await using RawLocalClient client = await RawLocalClient.ConnectAsync(mesh, LocalMeshes.Node, TestProcess.Served);
        await client.WriteRawAsync(hello);
        return Assert.IsType<Bye>(await client.ReadAsync());
    }

    // A Hello with any role byte, past the definition (which writes roles 1 and 2 only).
    private static byte[] HelloBytes(byte role, string name)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(name);
        int payload = 2 + 2 + 1 + 2 + utf8.Length + 2 + 4 + 32;
        byte[] frame = new byte[4 + payload];
        BinaryPrimitives.WriteUInt32BigEndian(frame, (uint)payload);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(6), 1);
        frame[8] = role;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(9), (ushort)utf8.Length);
        utf8.CopyTo(frame.AsSpan(11));
        return frame;
    }

    private static async Task<int> BytesUntilEndAsync(Stream stream)
    {
        byte[] buffer = new byte[64];
        int total = 0;
        while (true)
        {
            int read = await stream.ReadAsync(buffer).AsTask().WaitAsync(TestMesh.Deadline);
            if (read == 0)
                return total;
            total += read;
        }
    }
}

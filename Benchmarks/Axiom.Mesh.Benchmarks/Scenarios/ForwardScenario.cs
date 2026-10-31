using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Axiom.Mesh.Core;
using Axiom.Mesh.Protocol;
using Axiom.Mesh.Testing;
using Axiom.ProtoStream;
using Axiom.ProtoStream.Testing;

namespace Axiom.Mesh.Benchmarks.Scenarios;

/// <summary>
/// Where forwarding a 1 MiB object allocates (STRUCTURAL-DEBT.md 27), layer by layer: 64 <c>Object</c> messages of 1 MiB
/// between two AMP/1 sessions, first over an in-memory duplex pipe (the frame layer: ProtoStream's encoder, decoder and
/// pipes), then over a loopback TCP connection with TLS 1.3 (adds the socket, the stream adapters and <c>SslStream</c>).
/// What a node adds on top (intake, store, send path) is the loopback scenario's figure minus the second.
/// </summary>
internal static class ForwardScenario
{
    private const int Warmup = 8;
    private const int Objects = 64;

    public static async Task RunAsync(TextWriter output)
    {
        ObjectMessage[] messages = new ObjectMessage[Warmup + Objects];
        for (int i = 0; i < messages.Length; i++)
            messages[i] = ObjectMessage.From(ObjectBuilders.Chunk(Samples.Bytes(Limits.ChunkLength, 1000 + i)));

        TransportPair pair = InMemoryTransport.CreatePair();
        await using (Connection left = Connection.FromPipe(pair.Client, new ConnectionOptions()))
        await using (Connection right = Connection.FromPipe(pair.Server, new ConnectionOptions()))
        {
            double perObject = await MeasureAsync(left, right, messages).ConfigureAwait(false);
            output.WriteLine($"Result: frame layer in memory (encode, pipes, decode): {perObject / 1024:0} KiB per 1 MiB Object.");
        }

        (Stream client, Stream server, IDisposable cleanup) = await TlsPairAsync().ConfigureAwait(false);
        using (cleanup)
        await using (Connection left = Connection.FromStream(client, new ConnectionOptions()))
        await using (Connection right = Connection.FromStream(server, new ConnectionOptions()))
        {
            double perObject = await MeasureAsync(left, right, messages).ConfigureAwait(false);
            output.WriteLine($"Result: frame layer over loopback TCP with TLS 1.3 (adds socket, stream adapters, SslStream): {perObject / 1024:0} KiB per 1 MiB Object.");
        }
    }

    // Both Hellos, a warm-up, then the measured objects from left to right; the process's allocations per object.
    private static async Task<double> MeasureAsync(Connection left, Connection right, ObjectMessage[] messages)
    {
        Session<MeshMessage, MeshMessage> sender = await left.OpenAsync(Amp.Definition, ScenarioRunner.None).ConfigureAwait(false);
        Session<MeshMessage, MeshMessage> receiver = await right.OpenAsync(Amp.Definition, ScenarioRunner.None).ConfigureAwait(false);
        await sender.WriteAsync(Hello(TestIdentity.Of("alice-laptop")), ScenarioRunner.None).ConfigureAwait(false);
        await receiver.WriteAsync(Hello(TestIdentity.Of("bob-desktop")), ScenarioRunner.None).ConfigureAwait(false);
        await receiver.ReadAsync(ScenarioRunner.None).ConfigureAwait(false);
        await sender.ReadAsync(ScenarioRunner.None).ConfigureAwait(false);

        await TransferAsync(sender, receiver, messages.AsMemory(0, Warmup)).ConfigureAwait(false);
        long before = GC.GetTotalAllocatedBytes(precise: true);
        await TransferAsync(sender, receiver, messages.AsMemory(Warmup)).ConfigureAwait(false);
        return (GC.GetTotalAllocatedBytes(precise: true) - before) / (double)Objects;
    }

    private static async Task TransferAsync(Session<MeshMessage, MeshMessage> sender, Session<MeshMessage, MeshMessage> receiver, ReadOnlyMemory<ObjectMessage> messages)
    {
        Task writing = Task.Run(async () =>
        {
            for (int i = 0; i < messages.Length; i++)
                await sender.WriteAsync(messages.Span[i], ScenarioRunner.None).ConfigureAwait(false);
        });
        for (int i = 0; i < messages.Length; i++)
        {
            ProtocolReadResult<MeshMessage> read = await receiver.ReadAsync(ScenarioRunner.None).ConfigureAwait(false);
            if (read.Message is not ObjectMessage)
                throw new InvalidOperationException($"Expected an Object, read {read.Message}.");
        }

        await writing.ConfigureAwait(false);
    }

    private static Hello Hello(TestIdentity identity) =>
        new(identity.DeviceId, identity.UserId, 1, Capabilities.None, []);

    // A loopback TCP connection with TLS 1.3 on both ends, ALPN axiom-mesh/1, the server presenting a device certificate.
    private static async Task<(Stream Client, Stream Server, IDisposable Cleanup)> TlsPairAsync()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var client = new TcpClient { NoDelay = true };
        Task connecting = client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        TcpClient accepted = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
        accepted.NoDelay = true;
        await connecting.ConfigureAwait(false);

        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        X509Certificate2 certificate = DeviceCertificate.Create(key, DateTimeOffset.UtcNow);
        var alpn = new SslApplicationProtocol("axiom-mesh/1");
        var serverTls = new SslStream(accepted.GetStream(), leaveInnerStreamOpen: false);
        var clientTls = new SslStream(client.GetStream(), leaveInnerStreamOpen: false, (_, _, _, _) => true);
        await Task.WhenAll(
            serverTls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate, EnabledSslProtocols = SslProtocols.Tls13, ApplicationProtocols = [alpn] }),
            clientTls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "axiom-mesh", EnabledSslProtocols = SslProtocols.Tls13, ApplicationProtocols = [alpn] })).ConfigureAwait(false);
        return (clientTls, serverTls, certificate);
    }
}

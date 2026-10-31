using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB;
using Axiom.Mesh.Core;
using Axiom.Mesh.Core.Storage;
using Axiom.Mesh.Discovery;
using Axiom.Mesh.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Axiom.Mesh.TlsScan;

/// <summary>
/// One real node as the integration suite's <c>RealNode</c> runs it, minus the pipe: a TCP listener on the given endpoint
/// with the node's own MeshTls, a directory store and an LDB index in a temporary directory, keys in memory. Its identity is
/// Alice's laptop, and it knows Bob, so Bob's desktop's certificate passes the peer check (SPEC.md §4.3) and a scanner
/// presenting it sees full handshakes. It announces nothing and dials nobody.
/// </summary>
internal sealed class ScanTarget : IAsyncDisposable
{
    private static readonly TestIdentity Self = TestIdentity.Of("alice-laptop");
    private static readonly TestIdentity Scanner = TestIdentity.Of("bob-desktop");

    private readonly MeshNode node;
    private readonly Database database;
    private readonly string directory;

    private ScanTarget(MeshNode node, Database database, string directory)
    {
        this.node = node;
        this.database = database;
        this.directory = directory;
    }

    /// <summary>Where the node listens.</summary>
    public IPEndPoint Endpoint => node.MeshEndpoint;

    /// <summary>The node's device.</summary>
    public DeviceId Device => Self.DeviceId;

    /// <summary>Starts the node with its identity, trusting Bob.</summary>
    /// <param name="endpoint">Where to listen.</param>
    /// <param name="cancellationToken">Cancels starting.</param>
    /// <returns>The running target.</returns>
    public static async Task<ScanTarget> StartAsync(IPEndPoint endpoint, CancellationToken cancellationToken)
    {
        string directory = Path.Combine(Path.GetTempPath(), "axiom-mesh-tlsscan-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(directory, "data"));
        Database database = await Database.OpenAsync(Path.Combine(directory, "index"), DatabaseOptions.Default, cancellationToken).ConfigureAwait(false);
        var environment = new MeshNodeEnvironment
        {
            Files = new DirectoryObjectFiles(Path.Combine(directory, "data"), new SystemEntropy()),
            Database = database,
            Keys = InMemoryKeyStore.For(Self),
            Discovery = new CompositeDiscovery(new CompositeDiscoveryOptions
            {
                Parts = [new FakeDiscovery(new InMemoryNetwork(), new Announcement(Self.DeviceId, Self.UserId, endpoint, DiscoverySource.Mdns))],
                Time = TimeProvider.System,
                Cache = new AnnouncementCache(16, TimeSpan.Zero),
            }),
            Dialer = new TcpDialer(TimeProvider.System),
            Time = TimeProvider.System,
            Entropy = new SystemEntropy(),
            Loggers = NullLoggerFactory.Instance,
            Addresses = () => [endpoint.Address],
        };
        MeshNodeOptions options = MeshNodeOptions.Default with { MeshPort = endpoint.Port, ListenAddress = endpoint.Address, DeviceName = Self.Name };
        MeshNode node = await MeshNode.StartAsync(options, environment, cancellationToken).ConfigureAwait(false);
        await node.AdoptIdentityAsync(TestIdentity.RosterOf(Self.UserName), Self.UserName, cancellationToken).ConfigureAwait(false);
        await node.AcceptUserAsync(TestIdentity.RosterOf(Scanner.UserName), cancellationToken).ConfigureAwait(false);
        return new ScanTarget(node, database, directory);
    }

    /// <summary>Writes Bob's desktop's device certificate and private key as PEM, for a scanner's mutual TLS.</summary>
    /// <param name="path">The file.</param>
    /// <param name="cancellationToken">Cancels writing.</param>
    /// <returns>A task that completes when written.</returns>
    public async Task WriteClientPemAsync(string path, CancellationToken cancellationToken)
    {
        using ECDsa key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(Scanner.DevicePkcs8(), out _);
        using X509Certificate2 certificate = DeviceCertificate.Create(key, DateTimeOffset.UtcNow);
        await File.WriteAllTextAsync(path, certificate.ExportCertificatePem() + Environment.NewLine + key.ExportPkcs8PrivateKeyPem() + Environment.NewLine, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Stops the node and deletes what it kept.</summary>
    /// <returns>A task.</returns>
    public async ValueTask DisposeAsync()
    {
        await node.StopAsync(CancellationToken.None).ConfigureAwait(false);
        await database.DisposeAsync().ConfigureAwait(false);
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // Left in the temporary folder.
        }
    }
}

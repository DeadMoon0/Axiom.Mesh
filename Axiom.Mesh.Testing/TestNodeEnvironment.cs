using System;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB;
using Axiom.LDB.Storage.InMemory;
using Axiom.Mesh.Core;
using Axiom.Mesh.Core.Storage;
using Axiom.Mesh.Discovery;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Axiom.Mesh.Testing;

/// <summary>
/// What one node of a test mesh keeps across its restarts: its in-memory database environment, its object files and
/// its key store, on the mesh's one fake clock with seeded randomness. Each start opens a fresh database connection on
/// the same environment; a stop closes it, so a restarted node finds everything it stored.
/// </summary>
public sealed class TestNodeEnvironment
{
    private Database? database;

    /// <summary>The environment of a published test device.</summary>
    /// <param name="identity">The device; its key goes into the key store.</param>
    /// <param name="clock">The mesh's clock.</param>
    /// <param name="seed">The seed of the node's randomness.</param>
    public TestNodeEnvironment(TestIdentity identity, FakeTimeProvider clock, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(identity);
        Clock = clock ?? throw new ArgumentNullException(nameof(clock));
        Keys = InMemoryKeyStore.For(identity);
        Seed = seed;
    }

    /// <summary>The database's in-memory storage; it outlives every connection to it.</summary>
    public InMemoryStorageEnvironment Storage { get; } = new();

    /// <summary>The node's objects: in memory, unless the mesh put the node's store in a directory (<see cref="TestMeshOptions.StoreDirectories"/>).</summary>
    public IObjectFiles Files { get; init; } = new InMemoryObjectFiles();

    /// <summary>The node's keys.</summary>
    public InMemoryKeyStore Keys { get; }

    /// <summary>The mesh's clock.</summary>
    public FakeTimeProvider Clock { get; }

    /// <summary>The seed of the node's randomness.</summary>
    public ulong Seed { get; }

    /// <summary>Where the node logs; nowhere by default.</summary>
    public ILoggerFactory Loggers { get; init; } = NullLoggerFactory.Instance;

    /// <summary>Opens the database and hands out the environment for one start of the node.</summary>
    /// <param name="discovery">The node's discovery.</param>
    /// <param name="dialer">The node's dialler.</param>
    /// <param name="cancellationToken">Cancels opening.</param>
    /// <returns>The environment, in memory.</returns>
    /// <exception cref="InvalidOperationException">The node is running: close the previous start first.</exception>
    public async Task<MeshNodeEnvironment> OpenAsync(IDiscovery discovery, IDialer dialer, CancellationToken cancellationToken)
    {
        if (database is not null)
            throw new InvalidOperationException("The node's database is open: stop the node and close this environment before starting it again.");
        database = await Database.OpenAsync(Storage, DatabaseOptions.Default with { TimeProvider = Clock }, cancellationToken).ConfigureAwait(false);
        return new MeshNodeEnvironment
        {
            Files = Files,
            Database = database,
            Keys = Keys,
            Discovery = discovery,
            Dialer = dialer,
            Time = Clock,
            Entropy = new DeterministicEntropy(Seed),
            Loggers = Loggers,
            InMemory = true,
        };
    }

    /// <summary>Closes the database of the current start; the storage keeps what it holds.</summary>
    /// <returns>A task that completes when closed.</returns>
    public async ValueTask CloseAsync()
    {
        Database? open = Interlocked.Exchange(ref database, null);
        if (open is not null)
            await open.DisposeAsync().ConfigureAwait(false);
    }
}

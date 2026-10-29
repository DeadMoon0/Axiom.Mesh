using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core;

namespace Axiom.Mesh.Testing;

/// <summary>
/// One node of a <see cref="TestMesh"/>: its published identity, its address, what it keeps across restarts, and the
/// running node; and thin seams over it for tests that need content: create a Package, grant it, follow it, write
/// Entries, publish a folder, and wait until an object arrived.
/// </summary>
/// <remarks>
/// The seams sign with the published user key of the node's identity, as the Host will once it holds a user key; until
/// the local protocol carries these requests (LOCAL-SPEC.md §6), tests write through them.
/// </remarks>
public sealed class TestNode
{
    private readonly PackageBook packages;
    private MeshNode? node;

    internal TestNode(TestIdentity identity, IPEndPoint address, TestNodeEnvironment environment, FakeDiscovery discovery, InMemoryDialer dialer, PackageBook packages)
    {
        Identity = identity;
        Address = address;
        Environment = environment;
        Discovery = discovery;
        Dialer = dialer;
        this.packages = packages;
    }

    /// <summary>The node's name: its published device name.</summary>
    public string Name => Identity.Name;

    /// <summary>The published device and user it runs as.</summary>
    public TestIdentity Identity { get; }

    /// <summary>Its mesh endpoint on the in-memory network.</summary>
    public IPEndPoint Address { get; }

    /// <summary>What it keeps across restarts.</summary>
    public TestNodeEnvironment Environment { get; }

    /// <summary>Its discovery on the network's board.</summary>
    public FakeDiscovery Discovery { get; }

    /// <summary>Whether the node runs now.</summary>
    public bool IsRunning => node is not null;

    /// <summary>The running node.</summary>
    /// <exception cref="InvalidOperationException">The node is stopped.</exception>
    public MeshNode Node => node ?? throw new InvalidOperationException($"{Name} is stopped; start it with TestMesh.StartNodeAsync.");

    /// <summary>Its device id.</summary>
    public DeviceId Device => Identity.DeviceId;

    /// <summary>Its user id.</summary>
    public UserId User => Identity.UserId;

    internal InMemoryDialer Dialer { get; }

    /// <summary>
    /// Creates a Package owned by this node's user (SPEC.md §8.7): signs its first Manifest with the user key, follows
    /// it and stores the Manifest. Its id derives from the mesh's seed, so it is the same in every run.
    /// </summary>
    /// <param name="name">The Package's name.</param>
    /// <param name="kind">Archive or Channel.</param>
    /// <param name="retention">The owner's retention hint.</param>
    /// <param name="cancellationToken">Cancels waiting for the store.</param>
    /// <returns>The Package's id.</returns>
    public async Task<PackageId> CreatePackageAsync(string name, PackageKind kind, Retention retention, CancellationToken cancellationToken)
    {
        VerifiedManifest manifest = new ManifestDraft
        {
            Nonce = packages.NextNonce(),
            Kind = kind,
            Seq = 1,
            Name = name,
            Created = (ulong)Environment.Clock.GetUtcNow().ToUnixTimeMilliseconds(),
            Retention = retention,
        }.Sign(Identity.User);
        packages.Add(manifest);
        PackageId package = manifest.Content.Package;
        await Node.FollowAsync(package, cancellationToken).ConfigureAwait(false);
        if (!await Node.PutAsync(manifest, cancellationToken).ConfigureAwait(false))
            throw new TestMeshException($"{Name} did not store the Manifest of its new Package {package}.", "Read the node's facts; the store may be full.");
        return package;
    }

    /// <summary>
    /// Grants a published user rights to a Package this node's user owns (SPEC.md §8.9): signs the next Grant (seq one
    /// above the last this mesh signed) and stores it. Rights <see cref="GrantRights.None"/> revoke.
    /// </summary>
    /// <param name="package">The Package; created by a node of this mesh.</param>
    /// <param name="userName">The grantee: <c>alice</c>, <c>bob</c> or <c>eve</c>.</param>
    /// <param name="rights">The rights.</param>
    /// <param name="cancellationToken">Cancels waiting for the store.</param>
    /// <returns>The Grant.</returns>
    /// <exception cref="ArgumentException">The Package is not this node's user's, or the user is not a published one.</exception>
    public async Task<VerifiedGrant> GrantAsync(PackageId package, string userName, GrantRights rights, CancellationToken cancellationToken)
    {
        VerifiedManifest manifest = packages.ManifestOf(package);
        if (manifest.Content.Owner != User)
            throw new ArgumentException($"Package {package} is not {Identity.UserName}'s; only its owner grants it.", nameof(package));
        TestIdentity grantee = TestIdentity.All.FirstOrDefault(i => i.UserName == userName)
            ?? throw new ArgumentException($"No published test user is called '{userName}'; the users are alice, bob and eve.", nameof(userName));
        VerifiedGrant grant = new GrantDraft
        {
            Grantee = grantee.User.PublicKey,
            Rights = rights,
            Seq = packages.NextGrantSeq(package, grantee.UserId),
        }.Sign(Identity.User, manifest);
        if (!await Node.PutAsync(grant, cancellationToken).ConfigureAwait(false))
            throw new TestMeshException($"{Name} did not store the Grant of {package} to {userName}.", "The node must follow the Package and hold its Manifest.");
        return grant;
    }

    /// <summary>Follows a Package (LOCAL-SPEC.md §6.3); it replicates from every peer that lists it.</summary>
    /// <param name="package">The Package.</param>
    /// <param name="cancellationToken">Cancels waiting for the store.</param>
    /// <returns>A task that completes when the node follows it.</returns>
    public Task FollowAsync(PackageId package, CancellationToken cancellationToken) => Node.FollowAsync(package, cancellationToken);

    /// <summary>Writes an Entry on this node's device (SPEC.md §9.3).</summary>
    /// <param name="package">The Package.</param>
    /// <param name="payload">What it says; its kind is the Entry's kind.</param>
    /// <param name="cancellationToken">Cancels waiting for the store.</param>
    /// <returns>The Entry's hash.</returns>
    public Task<ObjectHash> WriteEntryAsync(PackageId package, EntryPayload payload, CancellationToken cancellationToken) =>
        Node.WriteEntryAsync(package, payload, cancellationToken);

    /// <summary>Publishes a folder: stores every object of it, then writes a Version naming its root Tree.</summary>
    /// <param name="package">The Archive.</param>
    /// <param name="folder">The folder.</param>
    /// <param name="cancellationToken">Cancels waiting for the store.</param>
    /// <returns>The Version's hash.</returns>
    public async Task<ObjectHash> PublishAsync(PackageId package, TreeFixture folder, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(folder);
        foreach (StorableObject stored in folder.Objects)
            await Node.PutAsync(stored, cancellationToken).ConfigureAwait(false);
        return await WriteEntryAsync(package, ObjectFixtures.Version(folder), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Waits until the node holds an Entry, from a peer or written here.</summary>
    /// <param name="hash">The Entry's hash.</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>A task that completes when it is stored.</returns>
    /// <exception cref="TestMeshException">It was not stored within <see cref="TestMesh.Deadline"/>.</exception>
    public Task WaitForEntryAsync(ObjectHash hash, CancellationToken cancellationToken) => WaitForObjectAsync(hash, cancellationToken);

    /// <summary>Waits until the node holds an object, from a peer or written here, on its <c>ObjectStored</c> facts.</summary>
    /// <param name="hash">The object's hash.</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>A task that completes when it is stored.</returns>
    /// <exception cref="TestMeshException">It was not stored within <see cref="TestMesh.Deadline"/>.</exception>
    public async Task WaitForObjectAsync(ObjectHash hash, CancellationToken cancellationToken)
    {
        MeshNode running = Node;
        using var deadline = new CancellationTokenSource(TestMesh.Deadline);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
        long seen = running.Facts.LastSequence;
        try
        {
            if (await running.ContainsAsync(hash, linked.Token).ConfigureAwait(false))
                return;
            await running.Facts.WaitForAsync(f => f.Kind == NodeFactKind.ObjectStored && f.Subject == hash.Value, seen, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TestMeshException(
                $"Waited {TestMesh.Deadline.TotalSeconds:0} s for {Name} to store {hash}, in vain. Replication: {running.Replication}. The last facts: {string.Join(" | ", running.Facts.Recent().TakeLast(16))}.",
                "Check that the node follows the Package and a peer that holds the object is established with it.",
                "Advance the mesh's Clock when the step waits for time (a backoff, a re-ask after 30 seconds).");
        }
    }

    internal void Running(MeshNode? running) => node = running;

    /// <inheritdoc />
    public override string ToString() => $"{Name} at {Address}";
}

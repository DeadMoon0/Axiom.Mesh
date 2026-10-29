using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB;
using Axiom.LDB.Entity;
using Axiom.LDB.Storage.InMemory;
using Axiom.Mesh.Core.Storage;
using Microsoft.Extensions.Time.Testing;

namespace Axiom.Mesh.Core.Tests.Support;

/// <summary>
/// The store of one node, entirely in memory: an in-memory LDB environment and object files that both survive a
/// restart, a fake clock, seeded entropy, and the writer, the store facade and intake on top.
/// </summary>
internal sealed class TestStore : IAsyncDisposable
{
    private TestStore(InMemoryStorageEnvironment environment, IObjectFiles files, FakeTimeProvider time, StoreOptions options, Database database, MeshDatabase mesh, ChangeBus bus, StoreWriter writer)
    {
        Environment = environment;
        Files = files;
        Time = time;
        Options = options;
        Database = database;
        Mesh = mesh;
        Bus = bus;
        Writer = writer;
        Store = new ObjectStore(writer, files);
        Intake = new Intake(writer, Wanted);
    }

    public InMemoryStorageEnvironment Environment { get; }

    public IObjectFiles Files { get; }

    public FakeTimeProvider Time { get; }

    public StoreOptions Options { get; }

    public Database Database { get; }

    public MeshDatabase Mesh { get; }

    public StoreWriter Writer { get; }

    public ObjectStore Store { get; }

    public Intake Intake { get; }

    public ChangeBus Bus { get; }

    public WantedSet Wanted { get; } = new();

    public long Now => Time.GetUtcNow().ToUnixTimeMilliseconds();

    public TrustSnapshot Trust => Writer.Trust;

    public static Task<TestStore> StartAsync(StoreOptions? options = null, IObjectFiles? files = null) =>
        StartAsync(new InMemoryStorageEnvironment(), files ?? new InMemoryObjectFiles(), new FakeTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds((long)Samples.Created)), options ?? StoreOptions.Default);

    /// <summary>Stops this node and starts it again on the same database environment, object files and clock.</summary>
    public async Task<TestStore> RestartAsync()
    {
        await DisposeAsync();
        return await StartAsync(Environment, Files, Time, Options);
    }

    public async ValueTask DisposeAsync()
    {
        await Writer.StopAsync();
        await Mesh.DisposeAsync();
        await Database.DisposeAsync();
    }

    /// <summary>The own identity: the user, and the device this node runs on.</summary>
    public Task<bool> IdentifyAsync(ISigner user, ISigner device) =>
        Writer.SendAsync(new IdentityCommand(new SelfIdentity(user.PublicKey, device.PublicKey.AsDeviceId())), CancellationToken.None);

    public Task<bool> PairAsync(ISigner user) => Writer.SendAsync(new PairCommand(user.PublicKey), CancellationToken.None);

    public Task<bool> FollowAsync(PackageId package) => Package(PackageCommandKind.Follow, package);

    public Task<bool> UnfollowAsync(PackageId package) => Package(PackageCommandKind.Unfollow, package);

    public Task<bool> ListAsync(PackageId package) => Package(PackageCommandKind.List, package);

    public Task<bool> SetPolicyAsync(PackageId package, LocalRetention policy) =>
        Writer.SendAsync(new PackageCommand(PackageCommandKind.SetPolicy, package, policy), CancellationToken.None);

    public Task<AdmitResult> PutAsync(StorableObject storable) => Store.PutAsync(storable, CancellationToken.None);

    public Task<AdmitResult> PutFromPeerAsync(StorableObject storable, ISigner peer) =>
        Store.PutAsync(storable, Origin.FromPeer(peer.PublicKey.AsDeviceId()), null, CancellationToken.None);

    /// <summary>Admits a verified Entry against the node's trust and stores it as written here.</summary>
    public async Task<AdmitResult> WriteAsync(VerifiedEntry entry) => await PutAsync(Samples.Admit(entry, Trust));

    public ValueTask<IntakeOutcome> ReceiveAsync(ReadOnlyMemory<byte> bytes, ObjectHash claimed, ISigner from) =>
        Intake.ReceiveAsync(claimed, bytes, Origin.FromPeer(from.PublicKey.AsDeviceId()), null, Now, CancellationToken.None);

    public ValueTask<IntakeOutcome> ReceiveAsync(ParsedObject parsed, ISigner from) => ReceiveAsync(parsed.Bytes, parsed.Hash, from);

    public Task<int> PinsAsync(PinCommandKind kind, PinReason reason, Hash32 source, params ObjectHash[] hashes) =>
        Writer.SendAsync(new PinCommand(kind, hashes, reason, source), CancellationToken.None);

    /// <summary>Every row of an entity's table, as of now.</summary>
    public async Task<List<T>> RowsAsync<T>()
        where T : class, IEntity<T>
    {
        await using LdbContext context = await LdbContext.OpenAsync(Database, CancellationToken.None);
        return await context.Set<T>().ReadOnlyQuery().ToListAsync(CancellationToken.None);
    }

    /// <summary>The pins held on an object, as reason and source.</summary>
    public async Task<List<(PinReason Reason, Hash32 Source)>> PinsOfAsync(ObjectHash hash)
    {
        var pins = new List<(PinReason, Hash32)>();
        foreach (PinRow row in await RowsAsync<PinRow>())
        {
            if (new ObjectHash(RowKeys.Hash(row.Hash)) == hash)
                pins.Add((row.Reason, RowKeys.Hash(row.Source)));
        }

        return pins;
    }

    public Task<bool> ContainsAsync(ObjectHash hash) => Store.ContainsAsync(hash, CancellationToken.None);

    private static async Task<TestStore> StartAsync(InMemoryStorageEnvironment environment, IObjectFiles files, FakeTimeProvider time, StoreOptions options)
    {
        Database database = await Database.OpenAsync(environment, DatabaseOptions.Default with { TimeProvider = time }, CancellationToken.None);
        MeshDatabase mesh = await MeshDatabase.OpenAsync(database, new DeterministicEntropy(7), CancellationToken.None);
        var bus = new ChangeBus();
        StoreWriter writer = await StoreWriter.StartAsync(mesh, files, options, time, bus, new Promises(), CancellationToken.None);
        var store = new TestStore(environment, files, time, options, database, mesh, bus, writer);
        return store;
    }

    private Task<bool> Package(PackageCommandKind kind, PackageId package) =>
        Writer.SendAsync(new PackageCommand(kind, package, LocalRetention.Default), CancellationToken.None);
}

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB;
using Axiom.Mesh.Core;
using Axiom.Mesh.Core.Storage;
using Axiom.Mesh.Testing;

namespace Axiom.Mesh.Benchmarks;

/// <summary>
/// One node's store on disk, as the daemon runs it: object files in a store directory (temporary file, flush, rename)
/// and the LDB index at full durability, behind the single store writer. The own identity is Alice's laptop, which holds
/// Alice's Roster and follows one Channel of hers, so its Entries are admitted. Deleted when disposed.
/// </summary>
internal sealed class DiskStore : IAsyncDisposable
{
    private DiskStore(string directory, Database database, MeshDatabase mesh, DirectoryObjectFiles files, StoreWriter writer, VerifiedManifest channel)
    {
        Directory = directory;
        Database = database;
        Mesh = mesh;
        Files = files;
        Writer = writer;
        Channel = channel;
        Store = new ObjectStore(writer, files);
    }

    public string Directory { get; }

    public Database Database { get; }

    public MeshDatabase Mesh { get; }

    public DirectoryObjectFiles Files { get; }

    public StoreWriter Writer { get; }

    public ObjectStore Store { get; }

    /// <summary>Alice's Channel, followed, its Manifest stored.</summary>
    public VerifiedManifest Channel { get; }

    public static async Task<DiskStore> StartAsync(string name)
    {
        string directory = Path.Combine(Path.GetTempPath(), "axiom-mesh-bench", name + "-" + Guid.NewGuid().ToString("N")[..8]);
        System.IO.Directory.CreateDirectory(directory);
        Database database = await Database.OpenAsync(Path.Combine(directory, "index"), DatabaseOptions.Default, CancellationToken.None).ConfigureAwait(false);
        MeshDatabase mesh = await MeshDatabase.OpenAsync(database, new SystemEntropy(), CancellationToken.None).ConfigureAwait(false);
        var files = new DirectoryObjectFiles(Path.Combine(directory, "data"), new SystemEntropy());
        StoreWriter writer = await StoreWriter.StartAsync(mesh, files, StoreOptions.Default, TimeProvider.System, new ChangeBus(), new Promises(), CancellationToken.None).ConfigureAwait(false);

        TestIdentity alice = TestIdentity.Of("alice-laptop");
        await writer.SendAsync(new IdentityCommand(new SelfIdentity(alice.User.PublicKey, alice.DeviceId)), CancellationToken.None).ConfigureAwait(false);
        await writer.SendAsync(new AdmitCommand(TestIdentity.RosterOf(alice.UserName), Origin.Local, null, null), CancellationToken.None).ConfigureAwait(false);
        VerifiedManifest channel = new ManifestDraft
        {
            Nonce = new PackageNonce(Hash32.Of("benchmark channel"u8)),
            Kind = PackageKind.Channel,
            Seq = 1,
            Name = "Benchmark",
            Created = (ulong)TestIdentity.Epoch.ToUnixTimeMilliseconds(),
            Retention = Retention.Default,
        }.Sign(alice.User);
        await writer.SendAsync(new PackageCommand(PackageCommandKind.Follow, channel.Content.Package, LocalRetention.Default), CancellationToken.None).ConfigureAwait(false);
        await writer.SendAsync(new AdmitCommand(channel, Origin.Local, null, null), CancellationToken.None).ConfigureAwait(false);
        return new DiskStore(directory, database, mesh, files, writer, channel);
    }

    /// <summary>An Entry of the Channel by Alice's laptop, admitted against the store's trust.</summary>
    /// <param name="clock">Its clock.</param>
    /// <param name="parents">Its parents.</param>
    /// <param name="text">What it says; makes two Entries differ.</param>
    /// <returns>The admitted Entry.</returns>
    public AdmittedEntry Entry(ulong clock, EntryParents parents, string text)
    {
        VerifiedEntry signed = new EntryDraft
        {
            Package = Channel.Content.Package,
            Clock = clock,
            Parents = parents,
            Body = new EntryBody(EntryKind.Message, System.Text.Encoding.UTF8.GetBytes(text), EntryReferences.None, default),
        }.Sign(TestKeys.AliceLaptop);
        return EntryAdmission.Decide(signed, Writer.Trust) is AdmissionDecision.Admit admit
            ? admit.Entry
            : throw new InvalidOperationException("The benchmark store does not admit its own Entry.");
    }

    public async ValueTask DisposeAsync()
    {
        await Writer.StopAsync().ConfigureAwait(false);
        await Mesh.DisposeAsync().ConfigureAwait(false);
        await Database.DisposeAsync().ConfigureAwait(false);
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
            // A file still held open by the system is left in the temporary folder.
        }
    }
}

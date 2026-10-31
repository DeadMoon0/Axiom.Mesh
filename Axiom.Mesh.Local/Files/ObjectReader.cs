using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Local;

/// <summary>
/// Reads stored objects for the file helpers, checked by hash and parsed (SPEC.md §8): straight from the store directory
/// (LOCAL-SPEC.md §9.7, layout 1) when the connection is a service and the Host says the directory is readable to it
/// (<c>HelloAck</c> capability bit 0), else with <c>GetObject</c>. A file that is not there (never stored, or evicted
/// since) falls back to <c>GetObject</c>, which answers <c>NotFound</c> for what is not stored.
/// </summary>
internal sealed class ObjectReader
{
    private readonly LocalClient client;
    private readonly string? storePath;

    private ObjectReader(LocalClient client, string? storePath)
    {
        this.client = client;
        this.storePath = storePath;
    }

    /// <summary>Whether objects are read from the store directory.</summary>
    public bool ReadsDirectly => storePath is not null;

    /// <summary>
    /// Whether a connection may read the store directory (LOCAL-SPEC.md §9.7, decision 17): a service, to which the Host reports
    /// the directory readable.
    /// </summary>
    /// <param name="host">The Host's <c>HelloAck</c>.</param>
    /// <returns><see langword="true"/> when direct reads apply.</returns>
    public static bool MayReadDirectly(HelloAck host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return host.Role == Role.Service && host.Capabilities.HasFlag(HostCapabilities.StoreReadable);
    }

    /// <summary>The file of an object in store layout 1: <c>&lt;path&gt;/objects/&lt;h0h1&gt;/&lt;h2h3&gt;/&lt;hash&gt;</c>.</summary>
    /// <param name="storePath">The store directory.</param>
    /// <param name="hash">The object.</param>
    /// <returns>The file's path.</returns>
    public static string FileOf(string storePath, ObjectHash hash)
    {
        string hex = hash.Value.ToHex();
        return Path.Combine(storePath, "objects", hex[..2], hex[2..4], hex);
    }

    /// <summary>A reader for a connection; asks for the store directory when direct reads apply.</summary>
    /// <param name="client">The connection.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The reader.</returns>
    public static async Task<ObjectReader> OpenAsync(LocalClient client, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (!MayReadDirectly(client.Host))
            return new ObjectReader(client, null);
        LocalResult<StorePath> path = await client.SendAsync(new GetStorePath(), cancellationToken).ConfigureAwait(false);
        return new ObjectReader(client, path.IsOk && path.Value.Layout == 1 ? path.Value.Path : null);
    }

    /// <summary>Reads, checks and parses one object.</summary>
    /// <param name="hash">The object.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The object, or why not: <c>NotFound</c> when it is not stored, <c>Invalid</c> when its bytes do not hash to it or do not parse.</returns>
    public async Task<LocalResult<ParsedObject>> ReadAsync(ObjectHash hash, CancellationToken cancellationToken)
    {
        byte[]? bytes = storePath is null ? null : await ReadFileAsync(FileOf(storePath, hash), cancellationToken).ConfigureAwait(false);
        if (bytes is null)
        {
            LocalResult<ReadOnlyMemory<byte>> read = await client.SendAsync(new GetObject { Hash = hash }, cancellationToken).ConfigureAwait(false);
            if (!read.IsOk)
                return LocalResult<ParsedObject>.Failure(read.Status, read.Error);
            bytes = read.Value.ToArray();
        }

        if (!HashedObject.TryVerify(bytes, hash, out HashedObject verified))
            return Refused(Status.Invalid, $"The bytes read for object {hash} do not hash to it.");
        return Objects.TryParse(verified, out ParsedObject? parsed, out ObjectFault fault)
            ? LocalResult<ParsedObject>.Ok(parsed)
            : Refused(Status.Invalid, $"Object {hash} is malformed: {fault.Detail}");
    }

    private static LocalResult<ParsedObject> Refused(Status status, string message) =>
        LocalResult<ParsedObject>.Failure(status, new Error { Message = message });

    // LOCAL-SPEC.md §9.7: opened by name so that the Host may delete it meanwhile; absent means not stored (any more).
    private static async Task<byte[]?> ReadFileAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, bufferSize: 0, useAsync: true);
            if (file.Length > Limits.MaxObjectLength)
                return null;
            byte[] bytes = new byte[file.Length];
            await file.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            return bytes;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException or EndOfStreamException)
        {
            return null;
        }
    }
}

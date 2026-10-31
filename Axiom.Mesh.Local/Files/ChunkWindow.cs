using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Local;

/// <summary>
/// The puts of one file in flight (STRUCTURAL-DEBT.md 57): at most a fixed number of <c>PutObject</c> requests outstanding,
/// each with a buffer of its own rented once and reused once the Host answered. A slot is freed oldest first, so the
/// first refusal reported is the first in file order; once a put is refused no buffer is handed out any more. Disposing
/// waits for every put still in flight, so no request outlives the call that made it and no buffer is returned while the
/// pipe may still read it.
/// </summary>
internal sealed class ChunkWindow : IAsyncDisposable
{
    private readonly LocalClient client;
    private readonly int size;
    private readonly Queue<(byte[] Buffer, Task<LocalResult<StoredObject>> Put)> inFlight;
    private readonly Stack<byte[]> free = new();

    /// <summary>A window over a connection.</summary>
    /// <param name="client">The connection the puts go over.</param>
    /// <param name="size">How many puts may be outstanding at once.</param>
    public ChunkWindow(LocalClient client, int size)
    {
        this.client = client;
        this.size = size;
        inFlight = new Queue<(byte[], Task<LocalResult<StoredObject>>)>(size);
    }

    /// <summary>The first refusal in file order as the file's result, once one was seen; no buffer is handed out after it.</summary>
    public LocalResult<StoredFile>? Failure { get; private set; }

    /// <summary>
    /// A buffer of <see cref="Chunker.MaxChunkObjectLength"/> bytes or more to read the next Chunk into: a free one, a new
    /// one while fewer than the window's size exist, else the oldest put's once it was answered.
    /// </summary>
    /// <returns>The buffer; <see langword="null"/> when a put was refused (see <see cref="Failure"/>).</returns>
    public async ValueTask<byte[]?> NextBufferAsync()
    {
        if (Failure is not null)
            return null;
        if (free.TryPop(out byte[]? spare))
            return spare;
        if (inFlight.Count < size)
            return ArrayPool<byte>.Shared.Rent(Chunker.MaxChunkObjectLength);

        (byte[] buffer, Task<LocalResult<StoredObject>> put) = inFlight.Dequeue();
        LocalResult<StoredObject> answered;
        try
        {
            answered = await put.ConfigureAwait(false);
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }

        if (answered.IsOk)
            return buffer;
        ArrayPool<byte>.Shared.Return(buffer);
        Failure = LocalResult<StoredFile>.Failure(answered.Status, answered.Error);
        return null;
    }

    /// <summary>Gives back a buffer that carried nothing (the end of the file was reached).</summary>
    /// <param name="buffer">A buffer this window handed out.</param>
    public void Return(byte[] buffer) => free.Push(buffer);

    /// <summary>Seals the Chunk in a buffer and puts it; the buffer stays the window's until the Host answered.</summary>
    /// <param name="buffer">A buffer this window handed out, holding the data behind the Chunk header.</param>
    /// <param name="length">The Chunk object's length: the header and the data.</param>
    /// <param name="cancellationToken">Cancels waiting for the answer; a put already written is still answered.</param>
    /// <returns>The Chunk's hash.</returns>
    public ObjectHash Put(byte[] buffer, int length, CancellationToken cancellationToken)
    {
        ObjectHash hash = Chunker.Seal(buffer.AsSpan(0, length));
        inFlight.Enqueue((buffer, client.SendAsync(new PutObject { Object = buffer.AsMemory(0, length) }, cancellationToken).AsTask()));
        return hash;
    }

    /// <summary>Waits for every put in flight, oldest first.</summary>
    /// <returns>The first refusal in file order, or <see langword="null"/> when every put succeeded.</returns>
    public async ValueTask<LocalResult<StoredFile>?> CompleteAsync()
    {
        while (Failure is null && inFlight.TryDequeue(out (byte[] Buffer, Task<LocalResult<StoredObject>> Put) next))
        {
            LocalResult<StoredObject> answered;
            try
            {
                answered = await next.Put.ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(next.Buffer);
            }

            if (!answered.IsOk)
                Failure = LocalResult<StoredFile>.Failure(answered.Status, answered.Error);
        }

        return Failure;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // A refusal, a cancellation or a read error ended the file early: what is still in flight is answered first.
        while (inFlight.TryDequeue(out (byte[] Buffer, Task<LocalResult<StoredObject>> Put) next))
        {
            await ((Task)next.Put).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            ArrayPool<byte>.Shared.Return(next.Buffer);
        }

        while (free.TryPop(out byte[]? spare))
            ArrayPool<byte>.Shared.Return(spare);
    }
}

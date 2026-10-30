using System;
using System.Threading;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// Where replication reports each object's way in or out (LOCAL-SPEC.md §5.16, topic <c>transfers</c> §7.2): wanted,
/// stored, sent, not found, cancelled, dropped. It stamps the time and hands the record to whoever listens (the local
/// port's event hub); with nobody listening it does nothing. Never blocks: the listener only queues.
/// </summary>
internal sealed class TransferLog
{
    private readonly TimeProvider time;
    private Action<Transfer>? listener;

    /// <summary>A log on the node's clock.</summary>
    /// <param name="time">The clock that stamps every record.</param>
    public TransferLog(TimeProvider time) => this.time = time ?? throw new ArgumentNullException(nameof(time));

    /// <summary>Hands every record to <paramref name="observer"/> from now on; replaces the previous one.</summary>
    /// <param name="observer">What to call; it must not block.</param>
    public void OnRecorded(Action<Transfer> observer) => Volatile.Write(ref listener, observer);

    /// <summary>Records one step of one object.</summary>
    /// <param name="peer">The peer device.</param>
    /// <param name="package">The Package it was wanted through; zero for a Roster or when not known.</param>
    /// <param name="hash">The object.</param>
    /// <param name="direction">In (this node wants or received it) or out (the peer wants or was sent it).</param>
    /// <param name="size">Its size; 0 when not known.</param>
    /// <param name="state">The step.</param>
    public void Record(DeviceId peer, PackageId package, ObjectHash hash, TransferDirection direction, long size, TransferState state)
    {
        if (Volatile.Read(ref listener) is not { } observer)
            return;
        observer(new Transfer
        {
            Peer = peer,
            PackageId = package,
            Hash = hash,
            Direction = direction,
            Size = (ulong)Math.Max(0, size),
            State = state,
            At = LocalRecords.Milliseconds(time.GetUtcNow()),
        });
    }
}

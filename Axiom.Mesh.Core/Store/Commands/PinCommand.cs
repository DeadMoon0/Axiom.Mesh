using System;
using System.Collections.Generic;

namespace Axiom.Mesh.Core;

/// <summary>
/// Pins, unpins or releases objects; completes with the number of pins added or removed. A client may hold only
/// <see cref="PinReason.User"/> and <see cref="PinReason.Service"/> pins: <see cref="PinReason.Head"/>,
/// <see cref="PinReason.Policy"/> and <see cref="PinReason.Outbound"/> are the node's own (LOCAL-SPEC.md §6.5).
/// </summary>
internal sealed class PinCommand : StoreCommand<int>
{
    /// <summary>A pin change.</summary>
    /// <param name="kind">What to do.</param>
    /// <param name="hashes">The objects; empty for <see cref="PinCommandKind.Release"/>.</param>
    /// <param name="reason">The reason; <see cref="PinReason.User"/> or <see cref="PinReason.Service"/> except for <see cref="PinCommandKind.Sent"/>.</param>
    /// <param name="source">The source (LOCAL-SPEC.md §5.11).</param>
    /// <exception cref="ArgumentException">A reason the node alone holds, except for <see cref="PinCommandKind.Sent"/>.</exception>
    public PinCommand(PinCommandKind kind, IReadOnlyList<ObjectHash> hashes, PinReason reason, Hash32 source)
    {
        ArgumentNullException.ThrowIfNull(hashes);
        if (kind != PinCommandKind.Sent && reason is not (PinReason.User or PinReason.Service))
            throw new ArgumentException($"Pins with reason {reason} are the node's own (LOCAL-SPEC.md §6.5); a client pins with User or Service.", nameof(reason));

        Kind = kind;
        Hashes = hashes;
        Reason = kind == PinCommandKind.Sent ? PinReason.Outbound : reason;
        Source = source;
    }

    /// <summary>What to do.</summary>
    public PinCommandKind Kind { get; }

    /// <summary>The objects.</summary>
    public IReadOnlyList<ObjectHash> Hashes { get; }

    /// <summary>The reason.</summary>
    public PinReason Reason { get; }

    /// <summary>The source.</summary>
    public Hash32 Source { get; }
}

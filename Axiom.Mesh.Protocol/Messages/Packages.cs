using System;
using System.Collections.Generic;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Protocol;

/// <summary>
/// <c>Packages</c> (2, SPEC.md §7.2), Established only: one page of a listing of the Packages the sender may list to
/// the peer. <see cref="ListingRule"/> checks the page sequence, the total and the time.
/// </summary>
public sealed class Packages : MeshMessage, IWireMessage<Packages>
{
    private readonly PackageState[] states;

    /// <summary>A page of a listing.</summary>
    /// <param name="page">The 0-based page number.</param>
    /// <param name="last">Whether this is the final page.</param>
    /// <param name="states">The Packages of this page: at most 64.</param>
    /// <exception cref="ArgumentException">The page holds more than 64 Packages.</exception>
    public Packages(ushort page, bool last, IReadOnlyList<PackageState> states)
        : this(page, last, MessageFields.Own(states, Limits.MaxPackagesPerPage, nameof(states)))
    {
    }

    private Packages(ushort page, bool last, PackageState[] states)
    {
        Page = page;
        Last = last;
        this.states = states;
    }

    /// <inheritdoc />
    public override ushort Id => MessageIds.Packages;

    /// <summary>The 0-based page number.</summary>
    public ushort Page { get; }

    /// <summary>Whether this is the final page; the receiver then replaces its view of the sender's state.</summary>
    public bool Last { get; }

    /// <summary>The Packages of this page.</summary>
    public IReadOnlyList<PackageState> States => states;

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, out Packages message)
    {
        ushort page = reader.UInt16BigEndian();
        bool last = reader.Boolean();
        int count = Wire.ReadListCount(ref reader, Limits.MaxPackagesPerPage);
        PackageState[] states = reader.Ok && MessageFields.Fits(ref reader, count, PackageState.MinLength) ? new PackageState[count] : [];
        for (int i = 0; i < states.Length && reader.Ok; i++)
            states[i] = PackageState.Read(ref reader);
        message = new Packages(page, last, reader.Ok ? states : []);
        return reader.Ok;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        writer.UInt16BigEndian(Page);
        writer.Boolean(Last);
        Wire.WriteListCount(ref writer, states.Length, Limits.MaxPackagesPerPage);
        foreach (PackageState state in states)
            state.Write(ref writer);
    }

    /// <inheritdoc />
    public override string ToString() => $"Packages page {Page}{(Last ? " (last)" : string.Empty)}: {states.Length}";
}

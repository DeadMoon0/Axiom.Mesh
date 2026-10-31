using System.Collections.Generic;
using Axiom.LDB;
using Axiom.Mesh.Core.Storage;

namespace Axiom.Mesh.Core;

/// <summary>Every table of the index inside one batch's write transaction.</summary>
internal sealed class BatchTables
{
    /// <summary>The tables of <paramref name="transaction"/>.</summary>
    /// <param name="transaction">A write transaction that holds every table of <see cref="Names"/>.</param>
    public BatchTables(WriteTransaction transaction)
    {
        Meta = new(transaction);
        Users = new(transaction);
        Devices = new(transaction);
        Packages = new(transaction);
        Grants = new(transaction);
        Heads = new(transaction);
        Entries = new(transaction);
        Objects = new(transaction);
        Pins = new(transaction);
        Peers = new(transaction);
        References = new(transaction);
        Parents = new(transaction);
    }

    /// <summary>The names of every table a batch may write.</summary>
    public static IReadOnlyCollection<string> Names =>
    [
        Rows<MetaRow>.Table, Rows<UserRow>.Table, Rows<DeviceRow>.Table, Rows<PackageRow>.Table, Rows<GrantRow>.Table,
        Rows<HeadRow>.Table, Rows<EntryRow>.Table, Rows<ObjectRow>.Table, Rows<PinRow>.Table, Rows<PeerRow>.Table,
        Rows<ReferenceRow>.Table, Rows<ParentRow>.Table,
    ];

    /// <summary>The <c>Meta</c> table.</summary>
    public Rows<MetaRow> Meta { get; }

    /// <summary>The <c>Users</c> table.</summary>
    public Rows<UserRow> Users { get; }

    /// <summary>The <c>Devices</c> table.</summary>
    public Rows<DeviceRow> Devices { get; }

    /// <summary>The <c>Packages</c> table.</summary>
    public Rows<PackageRow> Packages { get; }

    /// <summary>The <c>Grants</c> table.</summary>
    public Rows<GrantRow> Grants { get; }

    /// <summary>The <c>Heads</c> table.</summary>
    public Rows<HeadRow> Heads { get; }

    /// <summary>The <c>Entries</c> table.</summary>
    public Rows<EntryRow> Entries { get; }

    /// <summary>The <c>Objects</c> table.</summary>
    public Rows<ObjectRow> Objects { get; }

    /// <summary>The <c>Pins</c> table.</summary>
    public Rows<PinRow> Pins { get; }

    /// <summary>The <c>Peers</c> table.</summary>
    public Rows<PeerRow> Peers { get; }

    /// <summary>The <c>References</c> table.</summary>
    public Rows<ReferenceRow> References { get; }

    /// <summary>The <c>Parents</c> table.</summary>
    public Rows<ParentRow> Parents { get; }
}

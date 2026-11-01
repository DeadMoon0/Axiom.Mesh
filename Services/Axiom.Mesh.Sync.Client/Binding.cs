using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Axiom.Mesh.Local;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Sync;

/// <summary>A binding as the sync service keeps it (SYNC-SERVICE.md §4): one directory, one Archive, one branch.</summary>
public sealed record Binding : IWireRecord<Binding>
{
    /// <summary>The bound directory, absolute.</summary>
    public string Directory { get; init; } = "";

    /// <summary>The Archive it is kept in sync with.</summary>
    public PackageId PackageId { get; init; }

    /// <summary>The branch it follows and publishes to (PAYLOADS.md §2).</summary>
    public string Label { get; init; } = SyncProtocol.DefaultLabel;

    /// <summary>What it does with a symlink of the branch.</summary>
    public SymlinkPolicy Symlinks { get; init; } = SymlinkPolicy.AsFile;

    /// <summary>Its ignore patterns: exact names and <c>*.ext</c>, at most <see cref="SyncLimits.IgnorePatterns"/>.</summary>
    public IReadOnlyList<string> Ignore { get; init; } = [];

    /// <summary>Where it stands.</summary>
    public BindingState State { get; init; }

    /// <summary>The rights of this Host's user on the Archive: below <c>Write</c> the binding is read-only, it pulls and never publishes.</summary>
    public GrantRights Rights { get; init; }

    /// <summary>The Tree the directory last equalled as the service knows it; zero before the first run.</summary>
    public ObjectHash Base { get; init; }

    /// <summary>The branch's head for display (SPEC.md §9.5: the leaf highest in the total order); zero when the branch has no Version.</summary>
    public ObjectHash Head { get; init; }

    /// <summary>The leaves of the branch this Host holds; more than one until a merge resolves them.</summary>
    public ushort Leaves { get; init; }

    /// <summary>The files of the directory at the last run.</summary>
    public ulong Files { get; init; }

    /// <summary>Their bytes.</summary>
    public ulong Bytes { get; init; }

    /// <summary>Files changed in the directory that the service did not publish: a read-only binding's, or changed while a pull ran.</summary>
    public uint LocalChanges { get; init; }

    /// <summary>The conflicts the service recorded for the binding.</summary>
    public uint Conflicts { get; init; }

    /// <summary>When the last run ended, in milliseconds since the Unix epoch; 0 when none did.</summary>
    public ulong LastRun { get; init; }

    /// <summary>How many runs ended since the binding was made.</summary>
    public ulong Runs { get; init; }

    /// <summary>Whether a run is running or queued.</summary>
    public bool Pending { get; init; }

    /// <summary>Why the binding is in <see cref="BindingState.Error"/> or <see cref="BindingState.WaitingForObjects"/>; empty otherwise.</summary>
    public string Error { get; init; } = "";

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out Binding? value)
    {
        var read = new Binding
        {
            Directory = Wire.ReadString(ref reader, SyncLimits.PathLength),
            PackageId = PackageId.Read(ref reader),
            Label = Wire.ReadString(ref reader, SyncLimits.LabelLength),
            Symlinks = (SymlinkPolicy)reader.UInt8(),
            Ignore = SyncWire.ReadStrings(ref reader, SyncLimits.IgnorePatterns, SyncLimits.PatternLength),
            State = (BindingState)reader.UInt8(),
            Rights = (GrantRights)reader.UInt8(),
            Base = ObjectHash.Read(ref reader),
            Head = ObjectHash.Read(ref reader),
            Leaves = reader.UInt16BigEndian(),
            Files = reader.UInt64BigEndian(),
            Bytes = reader.UInt64BigEndian(),
            LocalChanges = reader.UInt32BigEndian(),
            Conflicts = reader.UInt32BigEndian(),
            LastRun = reader.UInt64BigEndian(),
            Runs = reader.UInt64BigEndian(),
            Pending = reader.Boolean(),
            Error = Wire.ReadString(ref reader, SyncLimits.ErrorLength),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        Wire.WriteString(ref writer, Directory, SyncLimits.PathLength);
        PackageId.Write(ref writer);
        Wire.WriteString(ref writer, Label, SyncLimits.LabelLength);
        writer.UInt8((byte)Symlinks);
        SyncWire.WriteStrings(ref writer, Ignore, SyncLimits.IgnorePatterns, SyncLimits.PatternLength);
        writer.UInt8((byte)State);
        writer.UInt8((byte)Rights);
        Base.Write(ref writer);
        Head.Write(ref writer);
        writer.UInt16BigEndian(Leaves);
        writer.UInt64BigEndian(Files);
        writer.UInt64BigEndian(Bytes);
        writer.UInt32BigEndian(LocalChanges);
        writer.UInt32BigEndian(Conflicts);
        writer.UInt64BigEndian(LastRun);
        writer.UInt64BigEndian(Runs);
        writer.Boolean(Pending);
        Wire.WriteString(ref writer, Error, SyncLimits.ErrorLength);
    }
}

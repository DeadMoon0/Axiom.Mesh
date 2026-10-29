using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>PackageChanged</c> (1024, LOCAL-SPEC.md §7.3): A Package changed: listed, created, followed, unfollowed, a new Manifest, Grant, Head or policy.</summary>
public sealed record PackageChanged : LocalEvent, IEvent<PackageChanged>
{
    /// <summary>The message id, 1024 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 1024;

    /// <summary>The topic whose subscriptions receive it (LOCAL-SPEC.md §7.2).</summary>
    public static Topic Topic => Topic.Packages;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>What changed (§14.12).</summary>
    public PackageChange Change { get; init; }

    /// <summary>The Package after the change.</summary>
    public required Package Package { get; init; }

    /// <summary>The new Grant, for change Grant.</summary>
    public Grant? Grant { get; init; }

    /// <summary>The new or changed Head, for change Head.</summary>
    public Head? Head { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out PackageChanged? value)
    {
        var read = new PackageChanged
        {
            Change = (PackageChange)reader.UInt8(),
            Package = LocalWire.ReadRecord<Package>(ref reader),
            Grant = reader.Boolean() ? LocalWire.ReadRecord<Grant>(ref reader) : null,
            Head = reader.Boolean() ? LocalWire.ReadHead(ref reader) : null,
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        writer.UInt8((byte)Change);
        Package.Write(ref writer);
        if (Grant is { } presentGrant)
        {
            writer.Boolean(true);
            presentGrant.Write(ref writer);
        }
        else
        {
            writer.Boolean(false);
        }
        if (Head is { } presentHead)
        {
            writer.Boolean(true);
            LocalWire.WriteHead(ref writer, presentHead);
        }
        else
        {
            writer.Boolean(false);
        }
    }
}

using System.Linq;
using Axiom.Mesh.Core;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Testing;

/// <summary>
/// The external service <c>cli</c> as a Host's default <c>config.json</c> lists it (plan decision D4), with a published
/// token: what a test mesh's local port is given so that the command line can write Entries and put files on its nodes.
/// </summary>
public static class TestCliService
{
    /// <summary>The <c>cli</c> service's token: 32 bytes of <c>0x11</c>. Published, so it guards nothing outside a test.</summary>
    public static ServiceToken Token { get; } = ServiceToken.FromBytes([.. Enumerable.Repeat((byte)0x11, 32)]);

    /// <summary>The service: external, the token above, kinds Archive and Channel, Entry kinds Version, Message and Redaction.</summary>
    public static LocalService Service { get; } = new()
    {
        Name = "cli",
        Provenance = ServiceProvenance.External,
        Token = Token,
        Kinds = [PackageKind.Archive, PackageKind.Channel],
        EntryKinds = [EntryKind.Version, EntryKind.Message, EntryKind.Redaction],
    };

    /// <summary>A local port that configures the <c>cli</c> service and keeps every other default.</summary>
    public static LocalPortOptions Local { get; } = LocalPortOptions.Default with { Services = [Service] };
}

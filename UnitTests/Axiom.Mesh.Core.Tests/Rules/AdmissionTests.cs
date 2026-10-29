using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>SPEC.md §4.6: a node bounds the connections it serves per peer address; a mesh node holds 4, since a device needs one.</summary>
public sealed class AdmissionTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    // Five connections from one address while none has finished its handshake: four are admitted, the fifth is refused
    // before anything is read; another address is admitted as before.
    [Fact]
    public async Task AdmissionPerAddressIs4()
    {
        await using TestMesh mesh = await Meshes.AliceKnowingBobAsync();
        MeshNode alice = mesh["alice-laptop"].Node;
        IPAddress crowded = IPAddress.Parse("10.0.0.50");
        var open = new List<Stream>();
        try
        {
            for (int port = 1; port <= 5; port++)
                open.Add(alice.ConnectInMemory(new IPEndPoint(crowded, 50000 + port)));
            await mesh.WaitForFactAsync("alice-laptop", f => f.Kind == NodeFactKind.Rejected, 0, "the fifth connection refused", None);
            open.Add(alice.ConnectInMemory(new IPEndPoint(IPAddress.Parse("10.0.0.51"), 50000)));
            await mesh.WaitUntilAsync("alice-laptop", n => n.Facts.Recent().Count(f => f.Kind == NodeFactKind.Accepted) == 5, "five admitted", None);

            NodeFact[] facts = [.. alice.Facts.Recent()];
            Assert.Equal(4, facts.Count(f => f.Kind == NodeFactKind.Accepted && f.Remote!.Address.Equals(crowded)));
            NodeFact refused = Assert.Single(facts, f => f.Kind == NodeFactKind.Rejected);
            Assert.Equal(crowded, refused.Remote!.Address);
            Assert.Equal("ClientLimit", refused.Detail);
            Assert.Single(facts, f => f.Kind == NodeFactKind.Accepted && f.Remote!.Address.Equals(IPAddress.Parse("10.0.0.51")));
        }
        finally
        {
            foreach (Stream stream in open)
                await stream.DisposeAsync();
        }
    }
}

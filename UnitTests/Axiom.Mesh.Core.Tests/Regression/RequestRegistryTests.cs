using System.Linq;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core.Tests.Regression;

/// <summary>
/// The dispatcher's table against the registry of LOCAL-SPEC.md §14.1 (<c>RequestRoutes.Handlers()</c>): since S0 every
/// request the protocol defines has a handler, the telemetry requests answering <c>NotFound</c>.
/// </summary>
public sealed class RequestRegistryTests
{
    // Damage: a request of the registry lost its row in Handlers() and was answered UnknownRequest, which a client reads as
    // "this Host does not serve it" (§1.5) and stops asking, although the Host is meant to serve it.
    [Fact]
    public void EveryRequestOfTheRegistryHasAHandler()
    {
        string[] unserved = [.. RequestRoutes.Default.All.Where(r => r.Handler is null).Select(r => r.Descriptor.Name)];

        Assert.Empty(unserved);
        Assert.Equal(RequestTable.Default.All.Count(), RequestRoutes.Handlers().Count);
    }
}

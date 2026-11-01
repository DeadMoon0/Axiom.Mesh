using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core.Tests.Regression;

/// <summary>
/// The dispatcher's lanes (LOCAL-SPEC.md §3.4, §6.7): <c>ServiceCall</c> waits up to 60 seconds on another process, so it
/// must never sit in the connection's ordered lane, where every later write request of the same app would queue behind it.
/// </summary>
public sealed class ServiceCallLaneTests
{
    // Damage: one slow service froze every request of the calling app for up to 60 seconds per call.
    [Fact]
    public void ServiceCallRunsOutsideTheOrderedLane_WhileEmitAndWriteEntryStayOrdered()
    {
        Assert.True(RequestRoutes.Default.TryGet(ServiceCall.Id, out RequestRoute call));
        Assert.True(RequestRoutes.Default.TryGet(Emit.Id, out RequestRoute emit));
        Assert.True(RequestRoutes.Default.TryGet(WriteEntry.Id, out RequestRoute write));

        Assert.False(call.Ordered);
        Assert.True(emit.Ordered);
        Assert.True(write.Ordered);
    }
}

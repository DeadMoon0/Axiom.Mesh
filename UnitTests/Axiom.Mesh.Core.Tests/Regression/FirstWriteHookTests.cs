using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Core.Tests.Regression;

/// <summary>
/// The seam the mesh port's ticket suppression hangs on (SPEC.md §4.2, OpenSslTickets): the armed action runs once, right
/// before the first write after arming and before its bytes reach the inner stream, never on a read, and its exception is
/// the write's.
/// </summary>
public sealed class FirstWriteHookTests
{
    // Damage: an action that ran on a read would run before the SSL handle exists; one that ran after the bytes, or on a
    // later write, would run after the ticket count was read; one that ran twice would set it on a closed handle.
    [Fact]
    public async Task TheArmedActionRunsOnceBeforeTheFirstWriteAndNeverOnAReadOrAgain()
    {
        var inner = new MemoryStream();
        inner.WriteByte(7);
        inner.Position = 0;
        var hook = new FirstWriteHook(inner);
        int runs = 0;
        long innerLengthWhenRun = -1;
        hook.Arm(() =>
        {
            runs++;
            innerLengthWhenRun = inner.Length;
        });

        Assert.Equal(7, hook.ReadByte());
        Assert.Equal(0, runs);

        await hook.WriteAsync(new byte[] { 2 });
        hook.Write(new byte[] { 3 }, 0, 1);
        await hook.WriteAsync(new byte[] { 4 }, 0, 1, CancellationToken.None);
        hook.Write([5]);

        Assert.Equal(1, runs);
        Assert.Equal(1, innerLengthWhenRun);
        Assert.Equal(new byte[] { 7, 2, 3, 4, 5 }, inner.ToArray());
    }

    // Damage: a swallowed exception would let the first flight out, and with it a handshake that ends in a ticket.
    [Fact]
    public async Task TheArmedActionsExceptionIsTheWritesAndTheBytesDoNotReachTheInnerStream()
    {
        var inner = new MemoryStream();
        var hook = new FirstWriteHook(inner);
        hook.Arm(() => throw new InvalidOperationException("no ticket count"));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await hook.WriteAsync(new byte[] { 1 }));

        Assert.Equal(0, inner.Length);
        hook.Write([2]); // the action ran once; the next write passes
        Assert.Equal(1, inner.Length);
    }
}

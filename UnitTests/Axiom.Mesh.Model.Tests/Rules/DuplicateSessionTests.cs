using System;
using Axiom.Mesh.Tests.Support;

namespace Axiom.Mesh.Tests.Rules;

/// <summary>SPEC.md §4.5: one session per device id, decided identically by both nodes.</summary>
public sealed class DuplicateSessionTests
{
    // §4.5: "1. If the old session has received nothing for 60 seconds, it is dead: keep the new, close the old. 2.
    // Otherwise, if both sessions were dialled by the same device, keep the newer. 3. Otherwise keep the session dialled by
    // the device with the lower device id (compared as 32-byte unsigned big-endian integers)." Device 0x7F… is lower than
    // 0x80….
    [Theory]
    // Rule 1: the old session was silent for 60 seconds, whoever dialled.
    [InlineData(60_000, (byte)0x7F, (byte)0x80, KeptSession.New)]
    // Rule 2: both dialled by the same device: the newer.
    [InlineData(1_000, (byte)0x80, (byte)0x80, KeptSession.New)]
    // Rule 3: the old was dialled by the lower id: the old.
    [InlineData(59_999, (byte)0x7F, (byte)0x80, KeptSession.Old)]
    // Rule 3: the new was dialled by the lower id: the new.
    [InlineData(0, (byte)0x80, (byte)0x7F, KeptSession.New)]
    public void Decide(int oldSilentMilliseconds, byte oldDialler, byte newDialler, KeptSession expected)
    {
        DeviceId oldDialledBy = new(RawObjects.Hash(oldDialler).Value);
        DeviceId newDialledBy = new(RawObjects.Hash(newDialler).Value);

        Assert.Equal(expected, DuplicateSessions.Decide(TimeSpan.FromMilliseconds(oldSilentMilliseconds), oldDialledBy, newDialledBy));
    }
}

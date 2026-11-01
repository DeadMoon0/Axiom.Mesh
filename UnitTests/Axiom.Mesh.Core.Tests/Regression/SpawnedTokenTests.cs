using System;
using System.Linq;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core.Tests.Regression;

/// <summary>
/// The token overlay of spawned services (LOCAL-SPEC.md §4.2, decision 27) as <c>ServiceDirectory</c> keeps it: a withdrawn
/// token reads as none, and none is exactly what <c>LocalHelloRule</c> refuses whatever a client presents.
/// </summary>
public sealed class SpawnedTokenTests
{
    private static readonly ServiceToken Child = ServiceToken.FromBytes([.. Enumerable.Repeat((byte)0x5a, 32)]);

    // Damage: a cleared spawned token compared equal to a client's all-zero token, so any program of the user could claim a
    // spawned service's name between two children.
    [Fact]
    public void AClearedTokenAdmitsNoHello_AllZeroIncluded()
    {
        var directory = new ServiceDirectory([new LocalService { Name = "chat", Provenance = ServiceProvenance.Spawned }]);
        directory.Set("chat", Child);
        directory.Clear("chat");
        LocalService chat = directory.Current.Single();

        Assert.True(chat.Token.IsNone);
        foreach (ServiceToken presented in new[] { ServiceToken.None, Child })
        {
            LocalHelloVerdict verdict = LocalHelloRule.Check(new Hello { Role = Role.Service, Name = "chat", Token = presented }, ProcessIdentity.Pipe(1, "alice", "/usr/bin/axiom-mesh-chat"), directory.Current, [], 0);
            Assert.False(verdict.IsAdmitted);
        }
    }

    // Damage: setting a token rewrote the configured list in place, so a connection admitted under the old list saw its
    // service's token change under it, and a name the configuration did not list as spawned could be given a token.
    [Fact]
    public void SettingATokenReplacesTheListAndOnlyForASpawnedName()
    {
        var external = new LocalService { Name = "cli", Provenance = ServiceProvenance.External, Token = Child };
        var directory = new ServiceDirectory([external, new LocalService { Name = "chat", Provenance = ServiceProvenance.Spawned }]);
        var before = directory.Current;

        directory.Set("chat", Child);

        Assert.True(before.Single(s => s.Name == "chat").Token.IsNone);
        Assert.Equal(Child, directory.Current.Single(s => s.Name == "chat").Token);
        Assert.Same(external, directory.Current.Single(s => s.Name == "cli"));
        Assert.Throws<ArgumentException>(() => directory.Set("cli", Child));
        Assert.Throws<ArgumentException>(() => directory.Clear("nobody"));
    }
}

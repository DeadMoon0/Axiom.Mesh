using System;
using System.Collections.Generic;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>
/// LOCAL-SPEC.md §3.4, the header of §6 and §2.2: before any handler, a request is checked for its role, for an identity
/// when it needs one, for a Draining session unless it is served then, for the confinement of user-key requests, and
/// for whether this Host serves it at all; the first check that fails answers it.
/// </summary>
public sealed class DispatchRuleTests
{
    [Theory]
    [InlineData((ushort)256, Role.User, true, false, true, Status.Ok)] // GetIdentity, all well
    [InlineData((ushort)257, Role.Service, true, false, true, Status.NotPermitted)] // CreateIdentity is the user role's only
    [InlineData((ushort)258, Role.User, false, false, true, Status.NoIdentity)] // RenameDevice needs an identity
    [InlineData((ushort)256, Role.User, false, false, true, Status.Ok)] // GetIdentity needs none
    [InlineData((ushort)256, Role.User, true, true, true, Status.Draining)] // a new request while Draining
    [InlineData((ushort)401, Role.User, true, true, true, Status.Ok)] // Unsubscribe is still served while Draining
    [InlineData((ushort)416, Role.User, true, true, true, Status.Ok)] // so is ListOperations
    [InlineData((ushort)417, Role.User, true, true, true, Status.Ok)] // and GetOperation
    [InlineData((ushort)418, Role.User, true, true, true, Status.Ok)] // and CancelOperation
    [InlineData((ushort)312, Role.User, true, false, false, Status.NotPermitted)] // SetGrant from an executable outside the confinement
    [InlineData((ushort)312, Role.User, true, false, true, Status.UnknownRequest)] // SetGrant permitted, but not served before M9
    [InlineData((ushort)304, Role.User, true, false, true, Status.UnknownRequest)] // ListPackages, not served before M9
    [InlineData((ushort)513, Role.User, true, false, true, Status.NotPermitted)] // WriteEntry is the service role's only
    public void TheChecksRunInOrder_RoleIdentityDrainingConfinementThenWhetherServed(ushort id, Role role, bool identity, bool draining, bool permitted, Status expected)
    {
        Assert.True(RequestRoutes.Default.TryGet(id, out RequestRoute route));
        Assert.Equal(expected, DispatchRule.Check(route, role, identity, draining, permitted).Status);
    }

    [Theory]
    [InlineData("", false, true)] // nothing listed: every executable may send them
    [InlineData("/usr/bin/axiom-mesh", true, true)] // the listed executable
    [InlineData("/usr/bin/other", true, false)] // another one
    [InlineData("", true, false)] // a connection whose executable the platform did not name, while some are listed
    public void UserKeyRequestsAreConfinedToListedExecutablesWhenAnyAreListed(string executable, bool confined, bool permitted)
    {
        IReadOnlyList<string> listed = confined ? ["/usr/bin/axiom-mesh"] : [];
        Assert.Equal(permitted, DispatchRule.Permits(executable, listed, StringComparison.Ordinal));
    }
}

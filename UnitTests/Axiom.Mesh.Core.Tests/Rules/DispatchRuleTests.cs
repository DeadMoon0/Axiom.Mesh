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
    [InlineData((ushort)312, Role.User, true, false, true, Status.Ok)] // SetGrant permitted and served
    [InlineData((ushort)307, Role.User, true, false, true, Status.Ok)] // UpdatePackage, served since phase 1b
    [InlineData((ushort)385, Role.User, true, false, true, Status.Ok)] // ServiceCall, served since phase 1b
    [InlineData((ushort)385, Role.Service, true, false, true, Status.NotPermitted)] // ServiceCall is the user role's only
    [InlineData((ushort)518, Role.Service, true, false, true, Status.Ok)] // Emit, served since phase 1b
    [InlineData((ushort)517, Role.Service, true, false, true, Status.Ok)] // AddTelemetry reaches its handler (which answers NotFound)
    [InlineData((ushort)513, Role.User, true, false, true, Status.NotPermitted)] // WriteEntry is the service role's only
    public void TheChecksRunInOrder_RoleIdentityDrainingConfinementThenWhetherServed(ushort id, Role role, bool identity, bool draining, bool permitted, Status expected)
    {
        Assert.True(RequestRoutes.Default.TryGet(id, out RequestRoute route));
        Assert.Equal(expected, DispatchRule.Check(route, role, identity, draining, permitted).Status);
    }

    // §3.4, §1.5: a request the protocol knows but a Host's table serves no handler for is answered UnknownRequest, last.
    [Fact]
    public void ARequestWithoutAHandlerIsUnknownRequest_AfterEveryOtherCheck()
    {
        var handlers = new Dictionary<ushort, RequestHandler>(RequestRoutes.Handlers());
        handlers.Remove(ServiceCall.Id);
        Assert.True(RequestRoutes.Build(handlers).TryGet(ServiceCall.Id, out RequestRoute route));

        Assert.Equal(Status.UnknownRequest, DispatchRule.Check(route, Role.User, true, false, true).Status);
        Assert.Equal(Status.NotPermitted, DispatchRule.Check(route, Role.Service, true, false, true).Status);
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

    // §2.2: ConfirmPairing spends the user key in modes ShowUser, JoinUser and AddDevice, not in ShowDevice, where the device
    // holds no user key; a pairing that is not the current one (no mode) counts as spending it. Other requests follow the table.
    [Theory]
    [InlineData((ushort)273, (byte)PairingMode.ShowUser, true)] // ConfirmPairing pairing with another user, showing
    [InlineData((ushort)273, (byte)PairingMode.JoinUser, true)] // the same, entering
    [InlineData((ushort)273, (byte)PairingMode.AddDevice, true)] // enrolling a device signs a new Roster
    [InlineData((ushort)273, (byte)PairingMode.ShowDevice, false)] // the device being enrolled has no user key
    [InlineData((ushort)273, (byte)0, true)] // no current pairing of that id: confined like the others
    [InlineData((ushort)312, (byte)0, true)] // SetGrant, always
    [InlineData((ushort)256, (byte)0, false)] // GetIdentity, never
    public void ConfirmPairingSpendsTheUserKeyInEveryModeButShowDevice(ushort id, byte mode, bool spends)
    {
        Assert.True(RequestRoutes.Default.TryGet(id, out RequestRoute route));
        LocalRequest request = id == ConfirmPairing.Id ? new ConfirmPairing { PairingId = 7 } : id == SetGrant.Id ? new SetGrant { PackageId = default, GranteeId = default, Rights = GrantRights.Read } : new GetIdentity();
        Assert.Equal(spends, DispatchRule.SpendsUserKey(route, request, mode == 0 ? null : (PairingMode)mode));
    }
}

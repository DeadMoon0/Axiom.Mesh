using Axiom.Mesh.Chat.Tests.Support;

namespace Axiom.Mesh.Chat.Tests.Rules;

/// <summary>
/// Who wrote a message (CHAT-SERVICE.md §4): the device's user and both names from the Rosters held here, a device in none
/// by its id alone; the owner's devices for SPEC.md §9.4, removed ones included (their Entries stay valid, §9.6).
/// </summary>
public sealed class AuthorResolutionTests
{
    // §4, SPEC.md §9.4, §9.6: a known device names its user and itself; an unknown one only its id; a removed device of the
    // owner is still the owner's.
    [Fact]
    public void AKnownDeviceNamesItsUserAndItself_AnUnknownOneOnlyItsId_ARemovedDeviceStaysTheOwners()
    {
        AuthorView desktop = AuthorResolution.Of(Facts.Desktop, Facts.Users, Facts.Devices);
        AuthorView stranger = AuthorResolution.Of(new DeviceId(Facts.Id(0x55)), Facts.Users, Facts.Devices);

        Assert.Equal((Facts.Bob, "bob", "desktop"), (desktop.UserId, desktop.UserName, desktop.DeviceName));
        Assert.Equal((new DeviceId(Facts.Id(0x55)), default(UserId), string.Empty), (stranger.DeviceId, stranger.UserId, stranger.UserName));
        Assert.True(AuthorResolution.IsDeviceOf(Facts.Phone, Facts.Alice, Facts.Devices));
        Assert.False(AuthorResolution.IsDeviceOf(Facts.Desktop, Facts.Alice, Facts.Devices));
    }
}

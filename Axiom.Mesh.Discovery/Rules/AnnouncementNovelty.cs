namespace Axiom.Mesh.Discovery;

/// <summary>What an announcement tells an <see cref="AnnouncementCache"/> that it did not know.</summary>
public enum AnnouncementNovelty
{
    /// <summary>The same claim was passed on within the repeat window: nothing new, pass nothing on.</summary>
    Duplicate = 0,

    /// <summary>The first claim for this device (or, for a static peer, this address) the cache holds.</summary>
    New = 1,

    /// <summary>The device claims another address or user than before; the newest claim replaced the old one.</summary>
    Changed = 2,

    /// <summary>The same claim, last passed on longer ago than the repeat window: worth passing on again.</summary>
    Repeated = 3,
}

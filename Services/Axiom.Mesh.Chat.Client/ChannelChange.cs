namespace Axiom.Mesh.Chat;

/// <summary>What happened to a Channel in a <see cref="ChannelEvent"/> (CHAT-SERVICE.md §6).</summary>
public enum ChannelChange : byte
{
    /// <summary>Assigned to nothing.</summary>
    Unassigned = 0,

    /// <summary>The service indexed the Channel's whole history held here; its timeline is complete from now on.</summary>
    Indexed = 1,

    /// <summary>The Channel is followed here (created, or started following).</summary>
    Followed = 2,

    /// <summary>The Channel is no longer followed; its messages stay indexed until it is dropped.</summary>
    Unfollowed = 3,

    /// <summary>The Host dropped the Channel; the service removed every row of it.</summary>
    Dropped = 4,

    /// <summary>The Channel's name changed.</summary>
    Renamed = 5,
}

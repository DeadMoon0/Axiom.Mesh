using Axiom.LDB.Entity;

namespace Axiom.Mesh.Chat;

/// <summary>A Channel the index knows (CHAT-SERVICE.md §10): its name and owner, whether it is followed, and its counts.</summary>
[LdbEntity("ChatChannels")]
internal sealed partial class ChannelRow
{
    /// <summary>The Package id.</summary>
    [Key]
    [Column(MaxLength = 32)]
    public byte[] PackageId { get; set; } = [];

    /// <summary>Its name, from its current Manifest.</summary>
    [Column(MaxLength = 255)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Its owner's user id.</summary>
    [Column(MaxLength = 32)]
    public byte[] OwnerId { get; set; } = [];

    /// <summary>Whether the Host follows it.</summary>
    public bool Followed { get; set; }

    /// <summary>The messages indexed, withdrawn ones included.</summary>
    public ulong Messages { get; set; }

    /// <summary>How many of them are withdrawn.</summary>
    public ulong Withdrawn { get; set; }

    /// <summary>The clock of the last message in the total order.</summary>
    public ulong LastClock { get; set; }

    /// <summary>The device of the last message; empty when it has none.</summary>
    [Column(MaxLength = 32)]
    public byte[]? LastDevice { get; set; }

    /// <summary>The hash of the last message; empty when it has none.</summary>
    [Column(MaxLength = 32)]
    public byte[]? LastHash { get; set; }

    /// <summary>Whether the whole history the Host held when it was last followed is indexed.</summary>
    public bool Indexed { get; set; }
}

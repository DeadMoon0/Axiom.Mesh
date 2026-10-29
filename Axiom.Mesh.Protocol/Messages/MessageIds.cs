namespace Axiom.Mesh.Protocol;

/// <summary>The message ids of SPEC.md §14.1, carried as a <c>u16</c> after the frame length (§5.1).</summary>
public static class MessageIds
{
    /// <summary><see cref="Protocol.Hello"/> (§7.1).</summary>
    public const ushort Hello = 1;

    /// <summary><see cref="Protocol.Packages"/> (§7.2).</summary>
    public const ushort Packages = 2;

    /// <summary><see cref="Protocol.PackageChanged"/> (§7.3).</summary>
    public const ushort PackageChanged = 3;

    /// <summary><see cref="Protocol.Have"/> (§7.4).</summary>
    public const ushort Have = 4;

    /// <summary><see cref="Protocol.Want"/> (§7.5).</summary>
    public const ushort Want = 5;

    /// <summary><see cref="ObjectMessage"/>, the message <c>Object</c> (§7.6).</summary>
    public const ushort Object = 6;

    /// <summary><see cref="Protocol.NotFound"/> (§7.7).</summary>
    public const ushort NotFound = 7;

    /// <summary><see cref="Protocol.Cancel"/> (§7.8).</summary>
    public const ushort Cancel = 8;

    /// <summary><see cref="Protocol.RosterQuery"/> (§7.9).</summary>
    public const ushort RosterQuery = 9;

    /// <summary><see cref="Protocol.ServiceOpen"/> (§7.10).</summary>
    public const ushort ServiceOpen = 10;

    /// <summary><see cref="Protocol.ServiceAccepted"/> (§7.10).</summary>
    public const ushort ServiceAccepted = 11;

    /// <summary><see cref="Protocol.ServiceRefused"/> (§7.10).</summary>
    public const ushort ServiceRefused = 12;

    /// <summary><see cref="Protocol.Ping"/> (§7.11).</summary>
    public const ushort Ping = 13;

    /// <summary><see cref="Protocol.Pong"/> (§7.11).</summary>
    public const ushort Pong = 14;

    /// <summary><see cref="Protocol.Bye"/> (§7.13).</summary>
    public const ushort Bye = 15;

    /// <summary><see cref="Protocol.RosterNotFound"/> (§7.9).</summary>
    public const ushort RosterNotFound = 16;

    /// <summary>The first id reserved for AMP/1 extensions; an unknown one is ignored (§1.4).</summary>
    public const ushort FirstExtension = 17;

    /// <summary>The last id reserved for AMP/1 extensions; ids above it are never assigned (§14.1).</summary>
    public const ushort LastExtension = 255;
}

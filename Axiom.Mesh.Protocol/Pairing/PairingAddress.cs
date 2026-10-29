using System;
using System.Globalization;

namespace Axiom.Mesh.Protocol;

/// <summary>
/// One address in a pairing code (PAIR-SPEC.md §2.2): a host name, an IPv4 address or an IPv6 address (with an optional
/// zone), and a TCP port.
/// </summary>
/// <param name="Host">The host, without brackets: letters, digits, <c>.</c> and <c>-</c>, or an IPv6 literal of hex digits, <c>:</c> and <c>.</c> with an optional <c>%zone</c>.</param>
/// <param name="Port">The TCP port, 1 to 65 535.</param>
[ClosedShape("A host and a port, which is all a dialler needs; a new address form is a new code version.")]
public readonly record struct PairingAddress(string Host, ushort Port)
{
    /// <summary>The longest host, the bound of a DNS name.</summary>
    public const int MaxHostLength = 253;

    /// <summary>Parses <c>host:port</c> or <c>[ipv6]:port</c>.</summary>
    /// <param name="text">The text.</param>
    /// <param name="address">The address, or <c>default</c>.</param>
    /// <returns><see langword="true"/> when the text is one well-formed address.</returns>
    public static bool TryParse(ReadOnlySpan<char> text, out PairingAddress address)
    {
        address = default;
        int colon = text.LastIndexOf(':');
        if (colon <= 0 || !TryParsePort(text[(colon + 1)..], out ushort port))
            return false;

        ReadOnlySpan<char> host = text[..colon];
        bool bracketed = host[0] == '[' && host[^1] == ']';
        if (bracketed)
            host = host[1..^1];
        if (!(bracketed ? IsIpv6Literal(host) : IsHostName(host)))
            return false;

        address = new PairingAddress(host.ToString(), port);
        return true;
    }

    /// <summary>Whether the host and port are well-formed, as <see cref="TryParse"/> requires.</summary>
    public bool IsWellFormed => Host is not null && Port != 0 && (Host.Contains(':', StringComparison.Ordinal) ? IsIpv6Literal(Host) : IsHostName(Host));

    /// <summary>The address as a code writes it: <c>host:port</c>, an IPv6 host in brackets.</summary>
    /// <returns>The text.</returns>
    public override string ToString() =>
        (Host ?? string.Empty).Contains(':', StringComparison.Ordinal)
            ? $"[{Host}]:{Port.ToString(CultureInfo.InvariantCulture)}"
            : $"{Host}:{Port.ToString(CultureInfo.InvariantCulture)}";

    // Decimal digits without sign or leading zero, 1 to 65 535.
    private static bool TryParsePort(ReadOnlySpan<char> text, out ushort port)
    {
        port = 0;
        if (text.IsEmpty || text.Length > 5 || text[0] == '0' || text.ContainsAnyExceptInRange('0', '9'))
            return false;
        int value = int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
        if (value > ushort.MaxValue)
            return false;
        port = (ushort)value;
        return true;
    }

    private static bool IsHostName(ReadOnlySpan<char> host)
    {
        if (host.IsEmpty || host.Length > MaxHostLength || host[0] is '.' or '-')
            return false;
        foreach (char c in host)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '.' or '-'))
                return false;
        }

        return true;
    }

    private static bool IsIpv6Literal(ReadOnlySpan<char> host)
    {
        int zone = host.IndexOf('%');
        ReadOnlySpan<char> address = zone < 0 ? host : host[..zone];
        ReadOnlySpan<char> zoneId = zone < 0 ? default : host[(zone + 1)..];
        if (address.Length < 2 || host.Length > MaxHostLength || !address.Contains(':') || (zone >= 0 && zoneId.IsEmpty))
            return false;
        foreach (char c in address)
        {
            if (!(char.IsAsciiHexDigit(c) || c is ':' or '.'))
                return false;
        }

        foreach (char c in zoneId)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-'))
                return false;
        }

        return true;
    }
}

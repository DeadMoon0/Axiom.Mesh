using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Axiom.Mesh.Protocol;

/// <summary>
/// The text one side of a pairing shows and the other enters (PAIR-SPEC.md §2):
/// <c>amp1:&lt;mode&gt;:&lt;host:port[,…]&gt;:&lt;device key base64url&gt;</c>, for example
/// <c>amp1:user:192.168.1.20:41810:BHq…</c>. The entering side dials one of the addresses and pins the device key in the
/// TLS handshake, so the code is the one thing the users must carry between their screens honestly.
/// </summary>
public sealed class PairingCode
{
    /// <summary>The prefix every code of this version starts with.</summary>
    public const string Prefix = "amp1";

    /// <summary>The longest code, the bound of <c>Pairing.code</c> (LOCAL-SPEC.md §5.15).</summary>
    public const int MaxLength = 512;

    /// <summary>The most addresses one code lists.</summary>
    public const int MaxAddresses = 8;

    private const string UserMode = "user";
    private const string DeviceMode = "device";

    /// <summary>The characters of a 65-byte key in base64url without padding.</summary>
    private const int EncodedKeyLength = 87;

    private readonly PairingAddress[] addresses;

    /// <summary>A code to show.</summary>
    /// <param name="mode">The showing side's mode: <see cref="PairingMode.ShowUser"/> or <see cref="PairingMode.ShowDevice"/>.</param>
    /// <param name="addresses">Where the showing side listens: 1 to 8 well-formed addresses.</param>
    /// <param name="deviceKey">The showing side's device (TLS) key, which the entering side pins.</param>
    /// <exception cref="ArgumentException">The mode does not show, an address is malformed, there are none or too many, or the text would exceed 512 characters.</exception>
    public PairingCode(PairingMode mode, IReadOnlyList<PairingAddress> addresses, PublicKey deviceKey)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        ArgumentNullException.ThrowIfNull(deviceKey);
        if (mode is not (PairingMode.ShowUser or PairingMode.ShowDevice))
            throw new ArgumentException($"A code is shown in mode ShowUser or ShowDevice, not {mode} (PAIR-SPEC.md §2).", nameof(mode));
        if (addresses.Count is 0 or > MaxAddresses)
            throw new ArgumentException($"A code lists 1 to {MaxAddresses} addresses, not {addresses.Count} (PAIR-SPEC.md §2.2).", nameof(addresses));

        this.addresses = new PairingAddress[addresses.Count];
        for (int i = 0; i < this.addresses.Length; i++)
        {
            if (!addresses[i].IsWellFormed)
                throw new ArgumentException($"Address '{addresses[i]}' is not a host and a port (PAIR-SPEC.md §2.2).", nameof(addresses));
            this.addresses[i] = addresses[i];
        }

        Mode = mode;
        DeviceKey = deviceKey;
        Text = Format(mode, this.addresses, deviceKey);
        if (Text.Length > MaxLength)
            throw new ArgumentException($"The code is {Text.Length} characters; at most {MaxLength} fit (LOCAL-SPEC.md §5.15). List fewer or shorter addresses.", nameof(addresses));
    }

    /// <summary>The showing side's mode.</summary>
    public PairingMode Mode { get; }

    /// <summary>The mode of the side that enters this code: <see cref="PairingMode.JoinUser"/> or <see cref="PairingMode.AddDevice"/>.</summary>
    public PairingMode EnteringMode => Mode == PairingMode.ShowUser ? PairingMode.JoinUser : PairingMode.AddDevice;

    /// <summary>Where the showing side listens, in the order to try.</summary>
    public IReadOnlyList<PairingAddress> Addresses => addresses;

    /// <summary>The showing side's device key, which the entering side's TLS handshake must find.</summary>
    public PublicKey DeviceKey { get; }

    /// <summary>The code as text.</summary>
    public string Text { get; }

    /// <summary>Parses a code entered by a user: exactly the form <see cref="Text"/> writes, nothing more.</summary>
    /// <param name="text">The text.</param>
    /// <param name="code">The code, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the text is a well-formed code.</returns>
    public static bool TryParse(string? text, [NotNullWhen(true)] out PairingCode? code)
    {
        code = null;
        if (text is null || text.Length > MaxLength || !text.StartsWith(Prefix + ":", StringComparison.Ordinal))
            return false;

        string rest = text[(Prefix.Length + 1)..];
        int modeEnd = rest.IndexOf(':', StringComparison.Ordinal);
        int keyStart = rest.LastIndexOf(':') + 1;
        if (modeEnd <= 0 || keyStart <= modeEnd + 1)
            return false;

        PairingMode mode = rest[..modeEnd] switch
        {
            UserMode => PairingMode.ShowUser,
            DeviceMode => PairingMode.ShowDevice,
            _ => PairingMode.None,
        };
        if (mode == PairingMode.None || !TryParseKey(rest.AsSpan(keyStart), out PublicKey? key))
            return false;

        string[] parts = rest[(modeEnd + 1)..(keyStart - 1)].Split(',');
        if (parts.Length > MaxAddresses)
            return false;
        var addresses = new PairingAddress[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!PairingAddress.TryParse(parts[i], out addresses[i]))
                return false;
        }

        code = new PairingCode(mode, addresses, key);
        return code.Text == text;
    }

    /// <inheritdoc />
    public override string ToString() => Text;

    private static string Format(PairingMode mode, PairingAddress[] addresses, PublicKey key)
    {
        var text = new StringBuilder(MaxLength);
        text.Append(Prefix).Append(':').Append(mode == PairingMode.ShowUser ? UserMode : DeviceMode).Append(':');
        for (int i = 0; i < addresses.Length; i++)
            text.Append(i == 0 ? string.Empty : ",").Append(addresses[i].ToString());
        // base64url (RFC 4648 §5) without padding: the standard alphabet with '-' and '_' for '+' and '/'.
        string base64 = Convert.ToBase64String(key.Encoded).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return text.Append(':').Append(base64).ToString();
    }

    private static bool TryParseKey(ReadOnlySpan<char> text, [NotNullWhen(true)] out PublicKey? key)
    {
        key = null;
        if (text.Length != EncodedKeyLength)
            return false;

        Span<char> padded = stackalloc char[EncodedKeyLength + 1];
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
                return false;
            padded[i] = c switch { '-' => '+', '_' => '/', _ => c };
        }

        padded[^1] = '=';
        Span<byte> bytes = stackalloc byte[PublicKey.Length];
        return Convert.TryFromBase64Chars(padded, bytes, out int written)
            && written == PublicKey.Length
            && PublicKey.TryParse(bytes, out key);
    }
}

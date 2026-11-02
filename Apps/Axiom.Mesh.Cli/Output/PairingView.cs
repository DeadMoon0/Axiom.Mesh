using System;
using System.Collections.Generic;
using System.Linq;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary>
/// A pairing (LOCAL-SPEC.md §5.15) for the user: where it stands, how the other side joins without typing (by this
/// device's id, PAIR-SPEC.md §2.4), the code to carry to the other screen where discovery does not reach, the string to
/// compare, and what to do next. The code is text; on a terminal it is also drawn in a box that is easy to read off
/// (a QR code is a later step, STRUCTURAL-DEBT.md). Pure.
/// </summary>
internal static class PairingView
{
    /// <summary>What <c>pair join</c> says when discovery heard no device a pairing could be with.</summary>
    public const string NothingDiscovered = "no device to pair with was discovered; ask the other side for its code: axiom-mesh pair join <code>";

    /// <summary>What to do after the discovered devices were listed.</summary>
    public const string JoinHint = "join one with its id or a prefix: axiom-mesh pair join <device>; where discovery does not reach, pair join <the code the other side shows>";

    /// <summary>The first line a showing side prints: how the other side joins without a code.</summary>
    /// <param name="self">This device's id, which discovery announces.</param>
    /// <returns>The line.</returns>
    public static string Invite(DeviceId self) =>
        $"on the other PC run: axiom-mesh pair join {Formats.Short(self.Value)}   (or, where discovery does not reach: pair join <the code below>)";

    /// <summary>Whether a discovered device is one a pairing could be with: in no Roster this Host holds, and heard at an address.</summary>
    /// <param name="peer">The device as <c>ListDiscovered</c> shows it.</param>
    /// <returns><see langword="true"/> when <c>pair join</c> lists it.</returns>
    public static bool IsJoinable(Peer peer) => !peer.Known && peer.Addresses.Count != 0;

    /// <summary>The discovered devices a pairing could be with, as a table.</summary>
    /// <param name="peers">The devices, already filtered by <see cref="IsJoinable"/>.</param>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<string> Discovered(IReadOnlyList<Peer> peers) => TextTable.Lines(
        ["DEVICE", "USER", "ADDRESSES", "LAST SEEN"],
        peers.Select(p => (IReadOnlyList<string>)[
            Formats.Short(p.DeviceId.Value),
            Formats.Short(p.UserId.Value),
            string.Join(",", p.Addresses),
            Formats.Time(p.LastSeen)]),
        NothingDiscovered);

    /// <summary>The pairing as names and values.</summary>
    /// <param name="pairing">The pairing.</param>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<string> Text(Pairing pairing)
    {
        var pairs = new List<(string, string)>
        {
            ("Pairing", $"{pairing.PairingId} ({Formats.Word(pairing.Mode)})"),
            ("State", Formats.Word(pairing.State)),
        };
        if (pairing.Code.Length != 0)
            pairs.Add(("Code", pairing.Code));
        if (pairing.Sas.Length != 0)
            pairs.Add(("Compare", pairing.Sas));
        if (!pairing.RemoteDeviceId.Value.IsZero)
            pairs.Add(("Other side", $"{(pairing.RemoteName.Length == 0 ? "(unnamed)" : pairing.RemoteName)}, device {Formats.Short(pairing.RemoteDeviceId.Value)}"));
        pairs.Add(("Expires", Formats.Time(pairing.Expires)));
        if (pairing.Failure.Length != 0)
            pairs.Add(("Failure", pairing.Failure));
        return TextTable.Pairs(pairs);
    }

    /// <summary>What to do next, in one line.</summary>
    /// <param name="pairing">The pairing.</param>
    /// <returns>The line.</returns>
    public static string Next(Pairing pairing) => pairing.State switch
    {
        PairingState.Waiting when pairing.Code.Length != 0 => "waiting for the other device: there, axiom-mesh pair join <this device> (or this code; or pair show --device there, then pair join here)",
        PairingState.Waiting => "connecting to the other device",
        PairingState.Confirm => $"compare {pairing.Sas} with the other screen; if both show it, run 'axiom-mesh pair confirm', else 'axiom-mesh pair cancel'",
        PairingState.Confirming => "confirmed here; waiting for the other side to confirm",
        PairingState.Paired => "paired",
        PairingState.Cancelled => "cancelled; nothing was stored",
        PairingState.Failed => "failed; nothing was stored: " + pairing.Failure,
        _ => Formats.Word(pairing.State),
    };

    /// <summary>The code drawn in a box, for a terminal: one character cell of margin, wrapped at 64 characters.</summary>
    /// <param name="code">The code.</param>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<string> Box(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        const int width = 64;
        string[] parts = [.. Enumerable.Range(0, (code.Length + width - 1) / width).Select(i => code.Substring(i * width, Math.Min(width, code.Length - (i * width))))];
        int inner = parts.Max(p => p.Length) + 2;
        var lines = new List<string> { "+" + new string('-', inner) + "+" };
        lines.AddRange(parts.Select(p => "| " + p.PadRight(inner - 2) + " |"));
        lines.Add("+" + new string('-', inner) + "+");
        return lines;
    }

    /// <summary>Whether a pairing has ended: Paired, Failed or Cancelled.</summary>
    /// <param name="pairing">The pairing.</param>
    /// <returns><see langword="true"/> when it has.</returns>
    public static bool IsEnded(Pairing pairing) => pairing.State is PairingState.Paired or PairingState.Failed or PairingState.Cancelled;
}

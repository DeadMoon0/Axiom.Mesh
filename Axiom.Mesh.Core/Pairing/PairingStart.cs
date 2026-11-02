using System.Collections.Generic;
using Axiom.Mesh.Local;
using LocalMode = Axiom.Mesh.Local.PairingMode;

namespace Axiom.Mesh.Core;

/// <summary>
/// What a client asked a pairing to start with (LOCAL-SPEC.md §6.2): the mode, and on the entering side either the code
/// the other side showed (<c>StartPairing</c>) or the device id and addresses discovery announced for it
/// (<c>StartPairingByDevice</c>, PAIR-SPEC.md §2.4). One shape for both requests, so the shell and its rule take one.
/// </summary>
/// <param name="Mode">The mode.</param>
/// <param name="Code">The code; empty unless <c>StartPairing</c> in an entering mode.</param>
/// <param name="Device">The device named; all zero unless <see cref="ByDevice"/>.</param>
/// <param name="Addresses">Its addresses; empty unless <see cref="ByDevice"/>.</param>
/// <param name="ByDevice">Whether the request was <c>StartPairingByDevice</c>.</param>
internal sealed record PairingStart(LocalMode Mode, string Code, DeviceId Device, IReadOnlyList<string> Addresses, bool ByDevice)
{
    /// <summary>The start a <c>StartPairing</c> asks for.</summary>
    /// <param name="request">The request.</param>
    /// <returns>The start.</returns>
    public static PairingStart Of(StartPairing request) => new(request.Mode, request.Code, default, [], ByDevice: false);

    /// <summary>The start a <c>StartPairingByDevice</c> asks for.</summary>
    /// <param name="request">The request.</param>
    /// <returns>The start.</returns>
    public static PairingStart Of(StartPairingByDevice request) => new(request.Mode, string.Empty, request.Device, request.Addresses, ByDevice: true);
}
